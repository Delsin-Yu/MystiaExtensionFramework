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

    void OnPreRewardProcessed(ref bool cancelInvocation) { }

    void OnPreDayEnd(ref Action onFinished, ref bool cancelInvocation) { }

    void OnPreAfterDayEnd(ref Action onFinished, ref bool cancelInvocation) { }
}
