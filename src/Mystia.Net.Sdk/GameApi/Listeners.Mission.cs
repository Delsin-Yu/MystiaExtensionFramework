using GameData.RunTime.Common;

using Mystia;

namespace Mystia.Listeners;

[AutoWire]
public interface IMissionListener
{
    /// <summary>
    /// A tracked mission recomputed its finish states (<c>RunTimeScheduler.TrackedMissionData.UpdateFinishStates</c>);
    /// <c>mission.conditionFinishStates</c> already holds the new states. Fired for every tracked mission that updates.
    /// </summary>
    void OnMissionFinishStatesUpdated(RunTimeScheduler.TrackedMissionData mission) { }
}
