using GameData.RunTime.Common;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mystia.Listeners;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The scheduler seams. Events, rewards and the two day end stages are driven by the scheduler's own static
/// entry points, so a listener sees them right before the game acts: a cancelled call returns before the
/// original body runs, and a mod that still wants the effect replays it through
/// <c>IDaySceneScheduleServices</c> (the reward replay is <c>ReplayReward</c>).
/// </summary>
internal static class ScheduleSeams
{
    [HarmonyPatch(typeof(RunTimeScheduler), nameof(RunTimeScheduler.ScheduleEvent))]
    private static class Scheduling
    {
        // ScheduleEvent is private in the game but public in the interop, so nameof reaches it. Rewriting
        // eventLabel queues the rewritten event, exactly like the scheduler rewrites its own postEvents.
        private static bool Prefix(ref string eventLabel)
        {
            var cancel = false;
            foreach (var listener in Dispatch.Instances<IScheduleListener>())
                listener.OnPreScheduleEvent(ref eventLabel, ref cancel);
            return !cancel;
        }
    }

    [HarmonyPatch(typeof(RunTimeScheduler), nameof(RunTimeScheduler.ProcessReward))]
    private static class Rewarding
    {
        // ProcessReward applies one node reward; the reward itself is only passed to the game, so the
        // listener gets the veto and the replaying mod keeps the reward data on its own side.
        private static bool Prefix()
        {
            var cancel = false;
            foreach (var listener in Dispatch.Instances<IScheduleListener>())
                listener.OnPreRewardProcessed(ref cancel);
            return !cancel;
        }
    }

    // OnDayEnd/OnAfterDayEnd only queue the callback for the day end chain; cancelling keeps this stage of
    // the chain from starting at all, and the listener's rewrite is the callback that ends up running.
    [HarmonyPatch(typeof(RunTimeScheduler), nameof(RunTimeScheduler.OnDayEnd))]
    private static class DayEnd
    {
        private static bool Prefix(ref Il2CppSystem.Action onFinish) => Guard(ref onFinish, after: false);
    }

    [HarmonyPatch(typeof(RunTimeScheduler), nameof(RunTimeScheduler.OnAfterDayEnd))]
    private static class AfterDayEnd
    {
        private static bool Prefix(ref Il2CppSystem.Action onFinish) => Guard(ref onFinish, after: true);
    }

    // The listener API carries a managed Action, the game hands in an Il2CppSystem.Action, and Interop only
    // converts managed -> il2cpp (Il2CppSystem.Action.op_Implicit). The game delegate is therefore wrapped in
    // a managed Action lazily, only once a listener is registered, and a listener that replaced the callback
    // is converted back. The wrapper invokes the game delegate it captured, so the usual
    // "keep the original and call it yourself" rewrite works; leaving the Action untouched keeps the game's
    // own delegate in place and costs no conversion.
    private static bool Guard(ref Il2CppSystem.Action onFinish, bool after)
    {
        var original = onFinish;
        Action? wrapper = null;
        Action? replacement = null;
        var cancel = false;
        foreach (var listener in Dispatch.Instances<IScheduleListener>())
        {
            wrapper ??= () => original?.Invoke();
            replacement ??= wrapper;
            if (after)
                listener.OnPreAfterDayEnd(ref replacement, ref cancel);
            else
                listener.OnPreDayEnd(ref replacement, ref cancel);
        }

        if (wrapper is not null && replacement is not null && !ReferenceEquals(replacement, wrapper))
            onFinish = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(replacement)!;
        return !cancel;
    }

    // Deliberately not migrated: the Reimu money box protection window. Its only entry is the compiler
    // generated local function ReimuProtection(Action) inside AddReimuPositiveSpellToWorkScene, which the
    // interop generator names Method_Internal_Static_Void_Action_0 (GameData.RunTime.Common.RunTimeScheduler;
    // the name is unique in the interop and the body is
    // SpawnSpecialGuestGroup -> TriggerPositiveBuff -> RepellAndLeaveNoPay). Reporting the window needs an
    // enter/exit notification pair that IScheduleListener does not have (revision v5 dropped it), and the
    // bridge does not change the SDK on its own, so the mod side keeps its own patch until the SDK grows a
    // member for it.
}
