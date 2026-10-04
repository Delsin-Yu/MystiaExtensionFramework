using HarmonyLib;
using Il2CppSystem.Linq;
using Mystia.Listeners;
using NightScene;
using NightScene.CookingUtility;
using NightScene.EventUtility;
using NightScene.GuestManagementUtility;
using NightScene.Tiles;
using UnityEngine;

using OnFailLoop = GameData.Profile.YuyukoBossData.__c__DisplayClass16_0.ObjectCompilerGeneratedNPrivateSealedIEnumerator1ObjectIEnumeratorIDisposableInObObObUnique;
using Retake = GameData.Profile.YuyukoBossData.__c__DisplayClass16_6;
using RunLoop = GameData.Profile.YuyukoBossData._MainChallengeLoop_d__16;
using StoryContext = GameData.Profile.YuyukoBossData.__c__DisplayClass16_0;

using Object = UnityEngine.Object;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The engine side of <see cref="IChallengeBossMirror"/>: the boss the running challenge fights, reached through
/// the two closures the challenge's main loop captured.
/// <para>
/// The life and the order flag live in different closures - the life in the loop's own one, which every phase
/// shares, and the order flag in the retake's, which only the retake's third phase has - so the mirror holds
/// both and answers for whichever of the two exists. The cooker swallow is not a closure at all: it is done at
/// the cooker layer (<c>CookSystemManager</c>, <c>TileManager</c> and the event manager's locked list), which is
/// the same layer the game's own swallow ends up in.
/// </para>
/// </summary>
internal sealed class YuyukoBossMirror : IChallengeBossMirror
{
    private static YuyukoBossMirror? _current;

    private readonly StoryContext _context;
    private readonly nint _contextPointer;
    private readonly RunLoop _loop;
    private Retake? _retake;

    private YuyukoBossMirror(RunLoop loop, StoryContext context)
    {
        _loop = loop;
        _context = context;
        _contextPointer = context.Pointer;
    }

    /// <summary>
    /// The mirror of the run whose shared closure is <paramref name="context"/>. One closure belongs to one run,
    /// so the mirror is cached by it and reused instead of rebuilt on every step; the retake closure is attached
    /// only once that phase exists, which is why it is re-attached on every step.
    /// </summary>
    internal static YuyukoBossMirror Reached(RunLoop loop, StoryContext context, Retake? retake)
    {
        if (_current is null || _current._contextPointer != context.Pointer)
            _current = new YuyukoBossMirror(loop, context);
        _current._retake = retake;
        return _current;
    }

    /// <summary>Forgets the mirror of a run that ended, so its closure is not kept alive between challenges.</summary>
    internal static void Dropped() => _current = null;

    public bool TryReadLife(out int life)
    {
        life = _context.yuyukoTotalLife;
        return true;
    }

    public bool WriteLife(int life)
    {
        // The game's own two steps when an order lands: the loop's life is reduced and the panel is told the
        // new progress. Both are needed, because the loop's field is what the game checks and the panel is what
        // the player sees.
        _context.yuyukoTotalLife = life;
        _context.statusDisplayer?.SetTargetProgress(life);
        return true;
    }

    public void WriteOrderAllowed(bool enabled)
    {
        // Only the retake has the flag, because only its third phase spawns the boss stand that competes with
        // the boss for the right to order.
        if (_retake is not null)
            _retake.ifYuyukoCouldOrder = enabled;
    }

    public bool SwallowCooker(int cookerIndex)
    {
        var desks = TileManager.Instance?.CookerDesks;
        if (desks is null || cookerIndex < 0 || cookerIndex >= desks.Length)
            return false;
        if (CookSystemManager.Instance?.GetCooker(desks[cookerIndex]) is not { } cooker)
            return false;

        // The lock is what makes the cooker unavailable; registering it before the effect means a cooker already
        // locked by the framework is left alone rather than locked twice.
        if (!ChallengeCookerSwallows.Arm(_context.eventManager, cookerIndex))
            return true;

        cooker.InterruptCook();
        cooker.visual?.HideCookerPermanent();
        return true;
    }

    // The run's own numbers live in the same closure, beside the boss's life: the takings are the event
    // manager's and the spell count the closure's own.
    public int EarnedFund
    {
        get => _context.eventManager?.EarnedFund ?? 0;
        set
        {
            if (_context.eventManager is { } manager)
                manager.EarnedFund = value;
        }
    }

    public int PositiveSpellCount
    {
        get => _context.positiveSpellCount;
        set => _context.positiveSpellCount = value;
    }

    /// <summary>
    /// Stops the run the way the game's own failure does before its story: the main loop is stopped and so are
    /// the three routines it yielded (a routine the loop is waiting on is not reachable through the loop's own
    /// frame, which is why each is stopped by name), and the retake's buff goes with them - its cooker locks stop
    /// and the effects it spawned are destroyed.
    /// </summary>
    public void StopRun()
    {
        if (_context.eventManager is not { } events)
            return;

        events.StopCoroutine(_loop.Cast<Il2CppSystem.Collections.IEnumerator>());
        Stop(events, _loop._mainLoop_5__6);
        Stop(events, _loop._negativeSpellLoop_5__7);
        Stop(events, _loop._standSpawnLoop_5__9);

        if (_retake is not { } retake)
            return;

        foreach (var coroutine in retake.lockCookerCorotine.ToArray())
            if (coroutine is not null)
                events.StopCoroutine(coroutine);
        foreach (var effect in retake.eatingGameObejct.ToArray())
            if (effect is not null)
                Object.Destroy(effect);
    }

    /// <summary>
    /// Carries the run's failure out: what the run registered with the scene is cleaned up and the game's own
    /// failure story is started. Only the callbacks this run's closure registered are removed, so the rest of the
    /// night's observers survive, and the story is started the way the game starts it.
    /// </summary>
    public void ReplayFailure()
    {
        if (_context.eventManager is not { } events || _context.guestsManager is not { } guests)
            return;

        if (guests.OnPositiveSpellTriggered is not null)
            foreach (var callback in guests.OnPositiveSpellTriggered.GetInvocationList())
                if (callback.Target?.Pointer == _contextPointer)
                    guests.OnPositiveSpellTriggered -= callback.Cast<Il2CppSystem.Action<SpecialGuestsController>>();
        if (_context.statusDisplayer is not null && events.OnFundUpdateCallback is not null)
            foreach (var callback in events.OnFundUpdateCallback.GetInvocationList())
                if (callback.Target?.Pointer == _context.statusDisplayer?.Pointer)
                    events.remove_OnFundUpdateCallback(callback.Cast<Il2CppSystem.Action<int>>());

        if (_context.statusDisplayer is not null)
            _context.statusDisplayer.gameObject.SetActive(false);
        if (_context.yuyuko is { } boss && boss.AllOrdersCount > 0)
            guests.CleanOrderInfo(boss);
        foreach (var guest in guests.AllGuestInDeskController.ToArray())
            guest.SetGuestCannotOrder();

        var failure = new OnFailLoop(0) { __4__this = _context };
        events.StartCoroutine(failure.Cast<Il2CppSystem.Collections.IEnumerator>());
    }

    private static void Stop(EventManager events, Coroutine? routine)
    {
        if (routine is not null)
            events.StopCoroutine(routine);
    }
}

/// <summary>
/// The boss itself: the run's own lookup (<c>GetControlled</c>) names the guest group the challenge fights, and
/// the mirrored handle is that group.
/// </summary>
internal static class ChallengeBossSeams
{
    /// <summary>
    /// The label the challenge's own main loop looks its boss up with. The game keeps one controlled guest per
    /// challenge under a fixed label, so the handle follows that lookup; a build that renamed the label would
    /// still have the run's closure name the boss, which is the fallback the run seam uses.
    /// </summary>
    private const string BossLabel = "Yuyuko";

    [HarmonyPatch(typeof(NightSceneDirector), nameof(NightSceneDirector.GetControlled))]
    private static class Controlled
    {
        private static void Postfix(string guestLabel, GuestGroupController __result)
        {
            if (guestLabel != BossLabel || __result is null)
                return;
            ChallengeTimeline.Shared.CaptureBoss(__result.Pointer, EntitySeams.GuestHandleOf(__result));
        }
    }
}

/// <summary>
/// The leave gate: the game's own way out of the night scene. While a challenge the framework owns still runs,
/// the leave is held - the challenge owns the scene until its exit - and the exit window, which the challenge's
/// own close opens, is the only place the services' switch can release it.
/// </summary>
internal static class ChallengeLeaveSeams
{
    /// <summary>
    /// The challenge's own close, which is where the exit window opens: the game closes the izakaya and then
    /// leaves the scene, and the leaving happens inside this call's own chain, so the window has to be open
    /// before it.
    /// </summary>
    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.CloseIzakayaAndLeaveChallengeMode))]
    private static class Closing
    {
        private static void Prefix() => ChallengeTimeline.Shared.ExitWindowOpened();
    }

    [HarmonyPatch(typeof(NightSceneDirector), nameof(NightSceneDirector.TryLeaveSession))]
    private static class Gate
    {
        /// <summary>The leave the gate held, kept so opening the switch can re-issue it.</summary>
        private static Il2CppSystem.Action? _held;

        private static bool Prefix(Il2CppSystem.Action onFinish)
        {
            var timeline = ChallengeTimeline.Shared;
            if (!timeline.MayLeaveScene())
            {
                // The leave did not run, so the game will not ask again on its own: the callback is kept and the
                // retry below re-issues the very same call once the gate opens.
                _held = onFinish;
                timeline.LeaveRetry = Retry;
                return false;
            }

            // This is the challenge's own leave, and the game loads the next scene synchronously inside it: the
            // listeners hear about it before that happens, so what they set is visible while the load runs.
            Dispatch.Run<IChallengeListener>(listener => listener.OnChallengeLeaveStarted());
            return true;
        }

        private static void Postfix() =>
            Dispatch.Run<IChallengeListener>(listener => listener.OnChallengeLeaveFinished());

        private static void Retry()
        {
            var held = _held;
            _held = null;
            if (held is null)
                return;

            // The gate opens from inside the game's own exit, where leaving the scene now would tear down the
            // running step, so the leave is re-issued on the main thread's queue instead. Without a queue (the
            // test host) there is nothing to tear down and the call is made directly.
            var queue = BridgeInstaller.MainThread;
            if (queue is null)
            {
                NightSceneDirector.Instance.TryLeaveSession(held);
                return;
            }

            queue.RunOnMainThread(() => NightSceneDirector.Instance.TryLeaveSession(held));
        }
    }
}

/// <summary>
/// The challenge's failure story: the game's own state machine for it, whose first step is everything the
/// failure does before it waits for the story to be acknowledged.
/// </summary>
internal static class ChallengeFailureSeams
{
    [HarmonyPatch(typeof(OnFailLoop), nameof(OnFailLoop.MoveNext))]
    private static class Story
    {
        private static void Prefix(OnFailLoop __instance)
        {
            // The routine's first step is the one that runs with the initial resume position, so it is the step
            // that starts the failure story; every later step is waiting for it.
            if (__instance.__1__state == 0)
                ChallengeTimeline.Shared.FailureStarted();
        }
    }
}

/// <summary>
/// The retake's boss buff: the game's own cleanup for it, which is where the buff's effects are taken back. The
/// interop spells that cleanup <c>Method_Internal_Void_0</c>; it is the closure's
/// <c>&lt;MainChallengeLoop&gt;g__OnBuffEnd|42</c>, which the startup check pins by the closure it lives in.
/// </summary>
internal static class ChallengeBuffSeams
{
    [HarmonyPatch(typeof(Retake), nameof(Retake.Method_Internal_Void_0))]
    private static class Cleanup
    {
        private static void Postfix()
        {
            // The game unlocks the cookers it swallowed from the same cleanup, so the framework's own swallows
            // are released with it and cannot outlive the buff. The release is the seam's, so it happens even
            // when the run already ended and the report below is dropped.
            ChallengeCookerSwallows.Release();
            ChallengeTimeline.Shared.BuffEnded();
        }
    }
}
