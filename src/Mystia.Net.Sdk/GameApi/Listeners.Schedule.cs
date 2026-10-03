using GameData.Profile;

using Mystia;

namespace Mystia.Listeners;

/// <summary>
/// Scheduler entry points that are still interceptable. The game's scheduler events and rewards are both
/// driven by an <c>Action</c> callback, so cancelling keeps the original callback from running; a mod that
/// wants to run it itself replays it through <c>IDaySceneScheduleServices</c>.
/// </summary>
[AutoWire]
public interface IScheduleListener
{
    /// <summary>A schedule event is about to play; <c>eventLabel</c> may be rewritten.</summary>
    void OnPreScheduleEvent(ref string eventLabel, ref bool cancelInvocation) { }

    /// <summary>
    /// A node reward is about to be processed. The reward travels with the notification, so a listener can
    /// recognise the one it cares about and — when it drops it — hand the same reward to
    /// <c>IDaySceneScheduleServices.ReplayReward</c> to have the game run it later.
    /// </summary>
    void OnPreRewardProcessed(ref SchedulerNode.Reward reward, ref bool cancelInvocation) { }

    void OnPreDayEnd(ref Action onFinished, ref bool cancelInvocation) { }

    void OnPreAfterDayEnd(ref Action onFinished, ref bool cancelInvocation) { }

    /// <summary>
    /// The Reimu money box protection window opened: the scheduler is about to add the Reimu positive spell to
    /// the running work scene, which spawns the money box guest, triggers its buff and lets it leave without
    /// pay. <see cref="OnReimuProtectionExited"/> runs once that whole body finished, so the pair brackets the
    /// window itself and not just the spawn.
    /// </summary>
    void OnReimuProtectionEntered() { }

    /// <summary>The Reimu money box protection window closed; see <see cref="OnReimuProtectionEntered"/>.</summary>
    void OnReimuProtectionExited() { }
}
