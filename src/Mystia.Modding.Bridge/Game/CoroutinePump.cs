using System.Collections;
using Mystia;
using UnityEngine;

namespace Mystia.Modding.Bridge;

// One instance owns the routines started through it, so StopAll stays inside one unit of work.
// The framework itself uses Shared; per-mod instances come from the mod context.
internal sealed class CoroutineScheduler : ICoroutineDispatcher
{
    private readonly bool _process;

    private CoroutineScheduler(bool process) => _process = process;

    // A scene scheduler follows the running scene session, so a routine started on its Owner dies with the
    // scene. A global scheduler is pinned to the process host, so its routines outlive every scene.
    public ICoroutineOwner Owner => _process ? CoroutinePump.ProcessHost : CoroutinePump.SceneHost;

    // The scene dispatcher: its Owner is the running scene session's host while a scene runs, so a routine
    // started on it dies with the scene, and the process host outside one.
    internal static readonly CoroutineScheduler Scene = new(process: false);

    // The framework wide instance every existing call site hands out; it is the scene dispatcher.
    internal static readonly CoroutineScheduler Shared = Scene;

    // The process dispatcher, for global loops that must survive every scene change.
    internal static readonly CoroutineScheduler Global = new(process: true);

    public CoroutineHandle Start(Func<ICoroutineDispatcher, IEnumerator> routine) =>
        CoroutinePump.Start(this, routine);

    public CoroutineHandle StartOn(ICoroutineOwner owner, Func<ICoroutineDispatcher, IEnumerator> routine) =>
        CoroutinePump.StartOn(this, owner, routine);

    public void Stop(CoroutineHandle handle) => CoroutinePump.Stop(this, handle);

    public void StopAll() => CoroutinePump.StopAll(this);

    public CoroutineAwait NextFrame => new(CoroutineAwait.WaitKind.NextFrame);

    public CoroutineAwait AfterSeconds(float seconds) => new(CoroutineAwait.WaitKind.Seconds, seconds);

    public CoroutineAwait AfterSecondsRealtime(float seconds) => new(CoroutineAwait.WaitKind.SecondsRealtime, seconds);

    public CoroutineAwait AfterFixedUpdate => new(CoroutineAwait.WaitKind.FixedUpdate);

    public CoroutineAwait AtEndOfFrame => new(CoroutineAwait.WaitKind.EndOfFrame);

    public CoroutineAwait Until(Func<bool> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return new CoroutineAwait(CoroutineAwait.WaitKind.Until, condition: condition);
    }

    public CoroutineAwait While(Func<bool> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return new CoroutineAwait(CoroutineAwait.WaitKind.While, condition: condition);
    }

    public CoroutineAwait Nested(Func<ICoroutineDispatcher, IEnumerator> routine)
    {
        ArgumentNullException.ThrowIfNull(routine);
        return new CoroutineAwait(CoroutineAwait.WaitKind.Nested, routine: routine);
    }
}

// Managed coroutine pump. Main thread only: no locking, and the collections are plain lists.
// Tick/FixedTick are driven by the host component in RuntimeInstall; Drain runs on unload.
internal static class CoroutinePump
{
    private sealed class Routine
    {
        internal int Id;
        internal CoroutineScheduler Scheduler = null!;
        internal Func<ICoroutineDispatcher, IEnumerator> Factory = null!;
        internal ICoroutineOwner? Owner;
        internal readonly List<IEnumerator> Stack = new();
        internal bool Started;
        internal bool Removed;
        internal CoroutineAwait.WaitKind Kind;
        internal float Remaining;
        internal int ReadyFrame;
        internal int FixedTicks;
        internal Func<bool>? Condition;
    }

    // The process level host. It lives as long as the pump does: MainThreadPump.OnDestroy drains the pump,
    // and that drain is what retires the owner and stops everything started on it.
    private sealed class ProcessOwner : ICoroutineOwner
    {
        internal static readonly ProcessOwner Instance = new();

        private bool _alive = true;

        internal bool IsAlive => _alive;

        internal void Retire() => _alive = false;
    }

    // One scene session's host. The pump hands out the running session and replaces the object when the game
    // moves on, so reference identity is the whole rule: a routine started on a replaced owner is gone.
    private sealed class SceneSessionOwner : ICoroutineOwner
    {
    }

    // Adapter for game-side enumerators. They are driven through MoveNext/Current, never
    // converted into an Il2Cpp delegate or handed to MonoBehaviour.StartCoroutine.
    private sealed class Il2CppEnumerator : IEnumerator
    {
        private readonly Il2CppSystem.Collections.IEnumerator _inner;

        internal Il2CppEnumerator(Il2CppSystem.Collections.IEnumerator inner) => _inner = inner;

        public bool MoveNext() => _inner.MoveNext();

        public object? Current => _inner.Current;

        public void Reset() => _inner.Reset();
    }

    private static readonly List<Routine> Active = new();
    private static Action<string>? _sink;
    private static int _nextId;
    private static int _fixedTicks;
    private static bool _ticking;

    // Scene sessions. The game loads one Unity scene per scene session (Splash, Main, Day, PrepNight, Night,
    // Staff, Result) and replaces it when it moves on, so the set of loaded scenes is the session identity.
    private static int[] _scenes = [];
    private static SceneSessionOwner? _scene;
    private static bool _derivedSessions = true;

    internal static void SetLogSink(Action<string> sink) => _sink = sink;

    // The host the global dispatcher binds to: the pump itself, alive for the whole process.
    internal static ICoroutineOwner ProcessHost => ProcessOwner.Instance;

    // The host the scene dispatcher binds to: the running scene session, or the process host when no session
    // runs (before the first scene, between scenes, and after LeaveScene).
    internal static ICoroutineOwner SceneHost => (ICoroutineOwner?)_scene ?? ProcessOwner.Instance;

    // Starts a new scene session, so the routines of the previous one stop on the next tick. A host that
    // knows the session boundaries better than the loaded scene set does (the scene loop host) drives
    // sessions with EnterScene/LeaveScene; the first such call turns the derivation below off.
    internal static void EnterScene()
    {
        _derivedSessions = false;
        _scene = new SceneSessionOwner();
        Log("scene session started by the host");
    }

    // Ends the running session. The scene dispatcher falls back to the process host until the next session.
    internal static void LeaveScene()
    {
        _derivedSessions = false;
        _scene = null;
        Log("scene session ended by the host");
    }

    internal static CoroutineHandle Start(CoroutineScheduler scheduler, Func<ICoroutineDispatcher, IEnumerator> routine) =>
        Launch(scheduler, routine, null);

    internal static CoroutineHandle StartOn(CoroutineScheduler scheduler, ICoroutineOwner owner, Func<ICoroutineDispatcher, IEnumerator> routine)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return Launch(scheduler, routine, owner);
    }

    private static CoroutineHandle Launch(CoroutineScheduler scheduler, Func<ICoroutineDispatcher, IEnumerator> routine, ICoroutineOwner? owner)
    {
        ArgumentNullException.ThrowIfNull(routine);
        GameBridgeHook.EnsurePump();
        SyncSceneSession(); // a routine started before the first tick still binds to the running scene
        var state = new Routine
        {
            Id = ++_nextId,
            Scheduler = scheduler,
            Factory = routine,
            Owner = owner,
        };

        // An owner that is already gone never runs, matching StartOn's contract. An owner the framework
        // never handed out can never become alive either, so it is reported instead of passing silently.
        if (owner is not null && !IsAlive(owner))
        {
            if (owner is not ProcessOwner and not SceneSessionOwner)
                Log("routine started on an owner the framework did not hand out, it will not run");
            return new CoroutineHandle(state.Id);
        }

        Active.Add(state);
        Run(state); // Unity runs a routine up to its first yield before Start returns.
        Sweep();
        return new CoroutineHandle(state.Id);
    }

    internal static void Tick(float delta)
    {
        SyncSceneSession();
        _ticking = true;
        try
        {
            var count = Active.Count;
            for (var i = 0; i < count; i++)
            {
                var state = Active[i];
                if (state.Removed)
                    continue;
                if (state.Owner is not null && !IsAlive(state.Owner))
                {
                    state.Removed = true;
                    continue;
                }
                if (state.Started && !IsReady(state, delta))
                    continue;
                Run(state);
            }
        }
        finally
        {
            _ticking = false;
        }

        Sweep();
    }

    internal static void FixedTick(float delta)
    {
        _ = delta;
        // Tick compares this counter, so a fixed wait resumes in the first Tick after a FixedUpdate.
        _fixedTicks++;
    }

    internal static void Stop(CoroutineScheduler scheduler, CoroutineHandle handle)
    {
        foreach (var state in Active)
        {
            if (state.Id != handle.Id || !ReferenceEquals(state.Scheduler, scheduler))
                continue;
            state.Removed = true;
            break;
        }

        Sweep();
    }

    internal static void StopAll(CoroutineScheduler scheduler)
    {
        foreach (var state in Active)
        {
            if (ReferenceEquals(state.Scheduler, scheduler))
                state.Removed = true;
        }

        Sweep();
    }

    internal static void Drain()
    {
        ProcessOwner.Instance.Retire();
        if (_ticking)
        {
            foreach (var state in Active)
                state.Removed = true;
            return;
        }

        Active.Clear();
    }

    // The pump answers owner liveness itself. A process host lives until the pump is drained, a scene host
    // until the pump moves to the next session, and an owner the pump did not hand out owns nothing, so a
    // routine started on one never runs.
    private static bool IsAlive(ICoroutineOwner? owner) => owner switch
    {
        ProcessOwner process => process.IsAlive,
        SceneSessionOwner scene => ReferenceEquals(scene, _scene),
        _ => false,
    };

    // Follows the loaded scene set to the running scene session. The rule is lenient in one direction and
    // strict in the other: a scene added on top of the loaded ones (a loading overlay) keeps the session,
    // because the scene the routines belong to is still there, while a scene that was replaced or unloaded
    // ends it, because a destroyed scene must not keep its routines running.
    private static void SyncSceneSession()
    {
        if (!_derivedSessions)
            return;
        if (!TryReadScenes(out var scenes))
            return;
        if (SameScenes(scenes, _scenes))
            return;

        var running = _scene;
        var additive = running is not null && ContainsAll(scenes, _scenes);
        _scenes = scenes;
        if (additive)
            return;
        if (scenes.Length == 0)
        {
            _scene = null; // nothing is loaded, so the scene dispatcher falls back to the process host
            return;
        }

        _scene = new SceneSessionOwner();
        Log("scene host changed; the routines bound to the previous scene stopped");
    }

    private static bool TryReadScenes(out int[] handles)
    {
        handles = [];
        try
        {
            var count = UnityEngine.SceneManagement.SceneManager.sceneCount;
            if (count <= 0)
                return true;
            var scenes = new int[count];
            for (var i = 0; i < count; i++)
                scenes[i] = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).handle;
            handles = scenes;
            return true;
        }
        catch (Exception error)
        {
            // Deriving sessions is a convenience: when the scene API is unavailable the pump stops guessing
            // instead of failing, and scene routines simply keep the process host as their owner.
            _derivedSessions = false;
            Log("scene session tracking disabled: " + error.GetBaseException().Message);
            return false;
        }
    }

    private static bool SameScenes(int[] left, int[] right)
    {
        if (left.Length != right.Length)
            return false;
        var a = (int[])left.Clone();
        var b = (int[])right.Clone();
        Array.Sort(a);
        Array.Sort(b);
        return a.AsSpan().SequenceEqual(b);
    }

    private static bool ContainsAll(int[] scenes, int[] previous)
    {
        foreach (var handle in previous)
        {
            if (Array.IndexOf(scenes, handle) < 0)
                return false;
        }

        return true;
    }

    private static bool IsReady(Routine state, float delta)
    {
        switch (state.Kind)
        {
            case CoroutineAwait.WaitKind.NextFrame:
            case CoroutineAwait.WaitKind.EndOfFrame:
                // The pump runs in Update, so both resume in the Update after the frame that yielded,
                // which is where Unity's WaitForEndOfFrame lands as well.
                return Time.frameCount >= state.ReadyFrame;
            case CoroutineAwait.WaitKind.Seconds:
                state.Remaining -= delta;
                return state.Remaining <= 0f;
            case CoroutineAwait.WaitKind.SecondsRealtime:
                state.Remaining -= Time.unscaledDeltaTime;
                return state.Remaining <= 0f;
            case CoroutineAwait.WaitKind.FixedUpdate:
                return _fixedTicks != state.FixedTicks;
            case CoroutineAwait.WaitKind.Until:
            case CoroutineAwait.WaitKind.While:
                return IsConditionMet(state);
            default:
                return true;
        }
    }

    private static bool IsConditionMet(Routine state)
    {
        bool value;
        try
        {
            value = state.Condition!();
        }
        catch (Exception error)
        {
            Fail(state, error);
            return false;
        }

        return state.Kind == CoroutineAwait.WaitKind.While ? !value : value;
    }

    private static void Run(Routine state)
    {
        if (!state.Started)
        {
            state.Started = true;
            if (!Push(state, state.Factory))
                return;
        }

        while (!state.Removed)
        {
            var frame = state.Stack[^1];
            bool moved;
            object? value;
            try
            {
                moved = frame.MoveNext();
                value = moved ? frame.Current : null;
            }
            catch (Exception error)
            {
                Fail(state, error);
                return;
            }

            if (!moved)
            {
                state.Stack.RemoveAt(state.Stack.Count - 1);
                if (state.Stack.Count == 0)
                {
                    state.Removed = true;
                    return;
                }

                continue; // the outer routine carries on where the nested one stopped
            }

            switch (value)
            {
                case null:
                    WaitFrame(state);
                    return;
                case CoroutineAwait token:
                    if (WaitToken(state, token))
                        return;
                    break;
                case WaitForSeconds seconds:
                    WaitSeconds(state, seconds.m_Seconds);
                    return;
                case WaitForSecondsRealtime realtime:
                    WaitSeconds(state, realtime.waitTime, realtime: true);
                    return;
                case WaitForEndOfFrame:
                    WaitFrame(state);
                    return;
                case WaitForFixedUpdate:
                    WaitFixed(state);
                    return;
                case Il2CppSystem.Collections.IEnumerator il2cpp:
                    state.Stack.Add(new Il2CppEnumerator(il2cpp));
                    break;
                case IEnumerator nested:
                    state.Stack.Add(nested);
                    break;
                default:
                    Warn(state, "unknown yield value " + value.GetType().FullName + ", treated as next frame");
                    WaitFrame(state);
                    return;
            }
        }
    }

    // true when the token paused the routine; false when it pushed a nested routine that runs on.
    private static bool WaitToken(Routine state, CoroutineAwait token)
    {
        switch (token.Kind)
        {
            case CoroutineAwait.WaitKind.NextFrame:
            case CoroutineAwait.WaitKind.EndOfFrame:
                WaitFrame(state);
                return true;
            case CoroutineAwait.WaitKind.Seconds:
                WaitSeconds(state, token.Seconds);
                return true;
            case CoroutineAwait.WaitKind.SecondsRealtime:
                WaitSeconds(state, token.Seconds, realtime: true);
                return true;
            case CoroutineAwait.WaitKind.FixedUpdate:
                WaitFixed(state);
                return true;
            case CoroutineAwait.WaitKind.Until:
            case CoroutineAwait.WaitKind.While:
                state.Kind = token.Kind;
                state.Condition = token.Condition;
                return true;
            case CoroutineAwait.WaitKind.Nested:
                Push(state, token.Routine!);
                return state.Removed;
            default:
                Warn(state, "unknown await token, treated as next frame");
                WaitFrame(state);
                return true;
        }
    }

    private static bool Push(Routine state, Func<ICoroutineDispatcher, IEnumerator> factory)
    {
        IEnumerator? nested;
        try
        {
            nested = factory(state.Scheduler);
        }
        catch (Exception error)
        {
            Fail(state, error);
            return false;
        }

        if (nested is null)
        {
            Warn(state, "routine produced no enumerator, stopped");
            state.Removed = true;
            return false;
        }

        state.Stack.Add(nested);
        return true;
    }

    private static void WaitFrame(Routine state)
    {
        state.Kind = CoroutineAwait.WaitKind.NextFrame;
        state.ReadyFrame = Time.frameCount + 1;
    }

    private static void WaitSeconds(Routine state, float seconds, bool realtime = false)
    {
        state.Kind = realtime ? CoroutineAwait.WaitKind.SecondsRealtime : CoroutineAwait.WaitKind.Seconds;
        state.Remaining = seconds;
    }

    private static void WaitFixed(Routine state)
    {
        state.Kind = CoroutineAwait.WaitKind.FixedUpdate;
        state.FixedTicks = _fixedTicks;
    }

    private static void Sweep()
    {
        if (_ticking)
            return;
        for (var i = Active.Count - 1; i >= 0; i--)
        {
            if (Active[i].Removed)
                Active.RemoveAt(i);
        }
    }

    private static void Fail(Routine state, Exception error)
    {
        Log("routine " + state.Id + " stopped: " + error);
        state.Removed = true;
    }

    private static void Warn(Routine state, string message) => Log("routine " + state.Id + ": " + message);

    private static void Log(string message) => _sink?.Invoke("coroutine " + message);
}
