using System.Collections;
using Mystia;
using UnityEngine;

namespace Mystia.Modding.Bridge;

// One instance owns the routines started through it, so StopAll stays inside one unit of work.
// The framework itself uses Shared; per-mod instances come from the mod context.
internal sealed class CoroutineScheduler : ICoroutineDispatcher
{
    internal static readonly CoroutineScheduler Shared = new();

    public CoroutineHandle Start(Func<ICoroutineDispatcher, IEnumerator> routine) =>
        CoroutinePump.Start(this, routine);

    public CoroutineHandle StartOn(Component owner, Func<ICoroutineDispatcher, IEnumerator> routine) =>
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
        internal Component? Target;
        internal bool Bound;
        internal readonly List<IEnumerator> Stack = new();
        internal bool Started;
        internal bool Removed;
        internal CoroutineAwait.WaitKind Kind;
        internal float Remaining;
        internal int ReadyFrame;
        internal int FixedTicks;
        internal Func<bool>? Condition;
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

    internal static void SetLogSink(Action<string> sink) => _sink = sink;

    internal static CoroutineHandle Start(CoroutineScheduler scheduler, Func<ICoroutineDispatcher, IEnumerator> routine) =>
        Launch(scheduler, routine, null, bound: false);

    internal static CoroutineHandle StartOn(CoroutineScheduler scheduler, Component owner, Func<ICoroutineDispatcher, IEnumerator> routine) =>
        Launch(scheduler, routine, owner, bound: true);

    private static CoroutineHandle Launch(CoroutineScheduler scheduler, Func<ICoroutineDispatcher, IEnumerator> routine, Component? target, bool bound)
    {
        ArgumentNullException.ThrowIfNull(routine);
        GameBridgeHook.EnsurePump();
        var state = new Routine
        {
            Id = ++_nextId,
            Scheduler = scheduler,
            Factory = routine,
            Target = target,
            Bound = bound,
        };

        // An owner that is already gone never runs, matching StartOn's contract.
        if (bound && !IsAlive(target))
            return new CoroutineHandle(state.Id);

        Active.Add(state);
        Run(state); // Unity runs a routine up to its first yield before Start returns.
        Sweep();
        return new CoroutineHandle(state.Id);
    }

    internal static void Tick(float delta)
    {
        _ticking = true;
        try
        {
            var count = Active.Count;
            for (var i = 0; i < count; i++)
            {
                var state = Active[i];
                if (state.Removed)
                    continue;
                if (state.Bound && !IsAlive(state.Target))
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
        if (_ticking)
        {
            foreach (var state in Active)
                state.Removed = true;
            return;
        }

        Active.Clear();
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

    // UnityEngine.Object compares equal to null once the native object is destroyed.
    private static bool IsAlive(Component? owner) => owner is not null && owner != null;

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
