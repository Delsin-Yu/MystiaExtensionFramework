using System.Reflection;
using GameData.Profile;
using GameData.RunTime.Common;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mystia.Listeners;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The listener pipelines of the scheduler seams. Both are pure dispatch, so the order the listeners are asked
/// in, what they see of each other's verdict and the scope of each interception stay testable without the game
/// running (see the panel and schedule suite).
/// </summary>
internal static class SchedulePipeline
{
    /// <summary>
    /// Asks every listener whether one node reward may be processed; false drops it. The reward travels by ref,
    /// so a listener that drops it can replay that very reward through <c>IDaySceneScheduleServices.ReplayReward</c>.
    /// </summary>
    internal static bool AllowReward(ref SchedulerNode.Reward reward)
    {
        var cancel = false;
        foreach (var listener in Dispatch.Instances<IScheduleListener>())
            listener.OnPreRewardProcessed(ref reward, ref cancel);
        return !cancel;
    }

    /// <summary>The Reimu money box protection window opened; every listener sees it.</summary>
    internal static void EnterReimuProtection() =>
        Dispatch.Run<IScheduleListener>(listener => listener.OnReimuProtectionEntered());

    /// <summary>The Reimu money box protection window closed; every listener sees it.</summary>
    internal static void ExitReimuProtection() =>
        Dispatch.Run<IScheduleListener>(listener => listener.OnReimuProtectionExited());
}

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
        // ProcessReward applies one node reward. The reward is handed to the listener by ref, which is what lets
        // a listener recognise the reward it means (a MoveToChallenge reward, say) and replay that same reward
        // later; the reward is only passed to the game after every listener left it alone.
        private static bool Prefix(ref SchedulerNode.Reward reward) => SchedulePipeline.AllowReward(ref reward);
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
}

/// <summary>
/// The Reimu money box protection window. Its only landing point is the compiler generated local function
/// <c>ReimuProtection(Action)</c> inside <c>AddReimuPositiveSpellToWorkScene</c>, which the interop exposes
/// under a numbered name; the window is that function's whole body
/// (<c>SpawnSpecialGuestGroup -> TriggerPositiveBuff -> RepellAndLeaveNoPay</c>), which is why the notification
/// pair brackets the function itself and not the spawn call inside it.
/// </summary>
internal static class ReimuProtectionWindow
{
    /// <summary>The interop name of the local function; the ordinal is part of it and moves with the build.</summary>
    internal const string MemberName = nameof(RunTimeScheduler.Method_Internal_Static_Void_Action_0);

    /// <summary>The name the game's own source gives that member: <c>&lt;AddReimuPositiveSpellToWorkScene&gt;g__ReimuProtection|160_0</c>.</summary>
    internal const string NativeName = "<AddReimuPositiveSpellToWorkScene>g__ReimuProtection|160_0";

    // Resolved and checked by name when this type is first touched, i.e. from the Prepare below. A missing
    // member, or one whose IL2CPP name is a different local function, throws here instead of patching the
    // wrong method.
    private static readonly MethodInfo Located = NamedSeams.LocateIl2Cpp(typeof(RunTimeScheduler), MemberName, NativeName);

    [HarmonyPatch(typeof(RunTimeScheduler), MemberName)]
    private static class Window
    {
        // Patch installation runs Prepare before it applies anything, so a window that cannot be located is
        // reported then and left uninstalled: the exception reaches the bridge's own installer, which logs it
        // per patch class (GameBridgeHook.Install).
        [HarmonyPrepare]
        private static void Prepare() => _ = Located;

        [HarmonyPrefix]
        private static void Enter() => SchedulePipeline.EnterReimuProtection();

        [HarmonyPostfix]
        private static void Exit() => SchedulePipeline.ExitReimuProtection();
    }
}

