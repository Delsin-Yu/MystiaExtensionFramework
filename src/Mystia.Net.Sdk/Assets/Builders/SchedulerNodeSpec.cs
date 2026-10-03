using Mystia.Data;

namespace Mystia.Assets;

// The scheduler node half of the game data builders. A mission node or an event node is a ScriptableObject the
// scheduler graph is built from, and a mod that ships one used to create it itself
// (ScriptableObject.CreateInstance<MissionNode>, the reward and condition structures by hand, the events that
// play a dialog resolved against the day scene's table). This surface is that work, named in values: the mod
// describes the node, the framework builds it and publishes it into the scheduler's own node table.
//
// The node description reuses the data face's own value types - SchedulerRewardData, MissionFinishConditionData,
// SchedulerTriggerData, SchedulerDayData, SchedulerEventData - because those are the same fields: that is how a
// mod that already describes a node for the data face (MissionNodeData / EventNodeData) passes the same values
// to the builder, and how the two paths keep meaning the same thing. What the builder adds over the data face is
// what a built node can carry and an injected one cannot: the fields the data face leaves out (a looped mission,
// a hidden receiver, an event's own scheduling flags) and the fact that a reference to another node resolves
// inside the one process that built both.
//
// Nothing here names an engine type. What a mod holds back is an opaque MissionNodeHandle / EventNodeHandle.

/// <summary>
/// The <c>SchedulerNode.Event.EventType</c> values a built node's event may carry, spelled out because a
/// <see cref="SchedulerEventData"/> carries the type as the game's own number.
/// </summary>
public static class NodeEventTypes
{
    /// <summary>The node plays nothing when it fires; the field is still filled in.</summary>
    public const int None = 0;

    /// <summary>
    /// A timeline asset. A timeline is an engine asset a mod cannot describe with values, so this builder
    /// refuses it; the mod that ships one still builds that node itself.
    /// </summary>
    public const int Timeline = 1;

    /// <summary>
    /// A dialog package, named by <see cref="SchedulerEventData.DialogPackage"/>. The name is resolved against
    /// the day scene's dialog table when the node is published, so a package built by this framework (or
    /// injected through the data face) under the same name is what plays.
    /// </summary>
    public const int Dialog = 2;
}

/// <summary>
/// Everything one mission node is built out of. The fields mirror the data face's <c>MissionNodeData</c> one by
/// one - a node described for the data face is a node described for the builder - plus the two fields the data
/// face does not carry.
/// <para>
/// The graph is the description's own limit: at most 256 rewards, 256 post rewards, 256 finish conditions and
/// 256 labels per connection list.
/// </para>
/// </summary>
public sealed record MissionNodeSpec
{
    /// <summary>
    /// The node's label: the key the scheduler's node table and the graph connections use. It is process wide,
    /// so it must be namespaced by the mod that owns the node; must not be empty.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>The label the node is shown with while the graph is edited; null falls back to the label.</summary>
    public string? DebugLabel { get; init; }

    /// <summary>The mission's name, written into the mission language table; null falls back to the label.</summary>
    public string? Name { get; init; }

    /// <summary>The mission's description, written into the mission language table.</summary>
    public string? Description { get; init; }

    /// <summary>The game's own <c>SchedulerNode.SchedulerType</c> (Main / Side / Kitsuna).</summary>
    public int MissionType { get; init; }

    /// <summary>The character the mission is given by (a character label), or null for a mission without a sender.</summary>
    public string? Sender { get; init; }

    /// <summary>The character the mission is given to (a character label; the game's own field is spelled <c>reciever</c>).</summary>
    public string? Receiver { get; init; }

    /// <summary>What finishing the mission gives.</summary>
    public IReadOnlyList<SchedulerRewardData>? Rewards { get; init; }

    /// <summary>What is given after the mission's performance (the post rewards the graph runs later).</summary>
    public IReadOnlyList<SchedulerRewardData>? PostRewards { get; init; }

    /// <summary>What has to happen for the mission to be finishable.</summary>
    public IReadOnlyList<MissionFinishConditionData>? FinishConditions { get; init; }

    /// <summary>The dialog the mission plays when it is finished; null for no event.</summary>
    public SchedulerEventData? MissionFinishEvent { get; init; }

    /// <summary>The dialog the mission plays when it fails; null for no event.</summary>
    public SchedulerEventData? MissionFailedEvent { get; init; }

    /// <summary>When the mission expires; required while <see cref="IsTimedMission"/> is true.</summary>
    public SchedulerTriggerData? MissionTimeLimit { get; init; }

    /// <summary>Whether the mission runs against a time limit.</summary>
    public bool IsTimedMission { get; init; }

    /// <summary>The game's own <c>MissionNode.MissionFailedAction</c> (BackToMainMenu / Rewind / None).</summary>
    public int MissionFailedAction { get; init; }

    /// <summary>Whether the mission is put back into the plan after it finished instead of being cleared.</summary>
    public bool Looped { get; init; }

    /// <summary>Whether the receiver is hidden in the mission panel.</summary>
    public bool HideReceiver { get; init; }

    /// <summary>The nodes that must be finished before this one is scheduled.</summary>
    public IReadOnlyList<string>? PreNodes { get; init; }

    /// <summary>The mission nodes that follow this one.</summary>
    public IReadOnlyList<string>? PostMissions { get; init; }

    /// <summary>The mission nodes that follow this one after its performance.</summary>
    public IReadOnlyList<string>? PostMissionsAfterPerformance { get; init; }

    /// <summary>The event nodes that follow this one.</summary>
    public IReadOnlyList<string>? PostEvents { get; init; }
}

/// <summary>
/// Everything one event node is built out of: when it fires, what it plays and where the graph goes afterwards.
/// The fields mirror the data face's <c>EventNodeData</c> one by one.
/// </summary>
public sealed record EventNodeSpec
{
    /// <summary>The node's label: the key the scheduler's node table and the graph connections use; must not be empty.</summary>
    public string? Label { get; init; }

    /// <summary>The label the node is shown with while the graph is edited; null falls back to the label.</summary>
    public string? DebugLabel { get; init; }

    /// <summary>The event's name, kept for symmetry with a mission node; the stock event nodes have no language entry.</summary>
    public string? Name { get; init; }

    /// <summary>The event's description, kept for symmetry with a mission node.</summary>
    public string? Description { get; init; }

    /// <summary>What the node plays; required, and either nothing or a dialog package.</summary>
    public SchedulerEventData? ScheduledEvent { get; init; }

    /// <summary>What makes the node fire; required.</summary>
    public SchedulerTriggerData? Trigger { get; init; }

    /// <summary>What the node gives when it fires.</summary>
    public IReadOnlyList<SchedulerRewardData>? Rewards { get; init; }

    /// <summary>What the node gives after its performance.</summary>
    public IReadOnlyList<SchedulerRewardData>? PostRewards { get; init; }

    /// <summary>Whether the node is scheduled once instead of on every trigger.</summary>
    public bool ScheduleOnce { get; init; }

    /// <summary>Whether the node is written into the archive once it fired.</summary>
    public bool SaveToArchiveOnce { get; init; }

    /// <summary>Whether the node completes itself when the day ends.</summary>
    public bool AutoCompleteAtDayEnd { get; init; }

    /// <summary>The game's own <c>EventNode.EventLockMode</c> (None / Lock / Unlock).</summary>
    public int EventLockMode { get; init; }

    /// <summary>The nodes that must be finished before this one is scheduled.</summary>
    public IReadOnlyList<string>? PreNodes { get; init; }

    /// <summary>The mission nodes that follow this one.</summary>
    public IReadOnlyList<string>? PostMissions { get; init; }

    /// <summary>The mission nodes that follow this one after its performance.</summary>
    public IReadOnlyList<string>? PostMissionsAfterPerformance { get; init; }

    /// <summary>The event nodes that follow this one.</summary>
    public IReadOnlyList<string>? PostEvents { get; init; }
}

/// <summary>
/// One scheduler node the builder built, of either kind. A node lives in one table keyed by its label
/// (<c>DataBaseScheduler.allNodes</c>), which is why both kinds answer to the same shape: the label they were
/// built under and whether the scheduler's table names them yet. A mod cannot build one itself - only
/// <see cref="IGameDataBuilder"/> hands one out.
/// </summary>
public abstract class SchedulerNodeHandle : IEquatable<SchedulerNodeHandle>
{
    internal SchedulerNodeHandle(string label) => Label = label;

    /// <summary>The node's label, as the description named it: the key the scheduler's node table uses.</summary>
    public string Label { get; }

    /// <summary>
    /// Whether the node is in the scheduler's table right now. The table is rebuilt when the scheduler
    /// initializes, so a node published before that is not published any more; the framework publishes what was
    /// built again after every rebuild, which is what this reports.
    /// </summary>
    public bool IsPublished { get; internal set; }

    /// <summary>
    /// Whether the two handles name the same node of the same kind. A handle is what it names, so two handles
    /// for one label compare equal and a handle is usable as a dictionary key; a mission handle never equals an
    /// event handle, even though both live in one table.
    /// </summary>
    public bool Equals(SchedulerNodeHandle? other) =>
        other is not null
        && other.GetType() == GetType()
        && string.Equals(Label, other.Label, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as SchedulerNodeHandle);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(GetType(), Label);

    /// <summary>The handle names the same node as <paramref name="right"/>.</summary>
    public static bool operator ==(SchedulerNodeHandle? left, SchedulerNodeHandle? right) => left is null ? right is null : left.Equals(right);

    /// <summary>The handle does not name the same node as <paramref name="right"/>.</summary>
    public static bool operator !=(SchedulerNodeHandle? left, SchedulerNodeHandle? right) => !(left == right);

    /// <summary>The node's label.</summary>
    public override string ToString() => Label;
}

/// <summary>One mission node the builder built; see <see cref="MissionNodeSpec"/>.</summary>
public sealed class MissionNodeHandle : SchedulerNodeHandle
{
    internal MissionNodeHandle(string label) : base(label)
    {
    }
}

/// <summary>One event node the builder built; see <see cref="EventNodeSpec"/>.</summary>
public sealed class EventNodeHandle : SchedulerNodeHandle
{
    internal EventNodeHandle(string label) : base(label)
    {
    }
}
