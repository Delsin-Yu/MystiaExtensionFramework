using System.Diagnostics.CodeAnalysis;

namespace Mystia.Scenes;

// The guest half of the entity handles (see EntitySession for the session the key is scoped to).
//
// A guest group used to travel through the SDK as the game's own GuestGroupController: a listener, a service
// query or a view handed the controller out and a mod could call anything on it. The handle replaces it. It is
// a value type a mod may compare and store, it names its entity only through GuestProxy (a read only projection
// plus the actions the framework supports), and it cannot be built outside the framework.
//
// The member surface below is the audited one: every read and every action is there because the mod of this
// repository (Managers/GuestFSM.cs, Managers/GuestService.cs, Listeners/GuestSync.cs, Listeners/WorkSync.cs,
// ResourceEx/SpellCollection/Spell_Mai.cs) actually uses it. The engine members nobody reads yet — the fifteen
// callbacks of the group, the dialog and payment members, the fund rate members — are deliberately left for the
// slice that needs them.

/// <summary>Whether a group is a group of normal guests or one special guest.</summary>
/// <remarks>Mirrors the game's <c>GuestsManager.GuestType</c>.</remarks>
public enum GuestKind
{
    Normal = 0,
    Special = 1,
}

/// <summary>How a group leaves the izakaya.</summary>
/// <remarks>Mirrors the game's <c>GuestGroupController.LeaveType</c>, value for value.</remarks>
public enum GuestLeaveType
{
    Move = 0,
    Fading = 1,
    Delete = 2,
    MoveToTargetPosition = 3,
}

/// <summary>The verdict a group gave the order it was served.</summary>
/// <remarks>
/// Mirrors the game's <c>GuestGroupController.EvaluationResult</c>, value for value. <see cref="None"/> is the
/// game's <c>Null</c>: no verdict yet.
/// </remarks>
public enum GuestEvaluation
{
    ExBad = 0,
    Bad = 1,
    Normal = 2,
    Good = 3,
    ExGood = 4,
    None = 5,
}

/// <summary>
/// One normal guest a group is rolled from or spawned with. It replaces the game's <c>NormalGuest</c> in the SDK:
/// the game answers everything else about a guest — its fund, its likes, its visual — from the id, which is the
/// only thing that has to travel.
/// </summary>
/// <param name="Id">The id of the guest in the game's character database.</param>
public readonly record struct GuestDescription(int Id)
{
    /// <summary>Whether this description names a guest. A negative id names none.</summary>
    public bool IsEmpty => Id < 0;

    public override string ToString() => IsEmpty ? "guest (none)" : $"guest {Id}";
}

/// <summary>
/// An opaque guest group of the running scene. A mod receives it from a listener, asks a service or a view for
/// it, and stores and compares it freely; it cannot build one, and it never sees the controller behind it.
/// <para>
/// The handle stays meaningful for exactly the night it was minted in: after the next scene loop starts,
/// <see cref="TryGet"/> answers false. That is the only place a handle is validated, so a callback may hand the
/// same handle to several reads without paying for the check each time.
/// </para>
/// </summary>
public readonly struct GuestHandle : IEquatable<GuestHandle>
{
    internal GuestHandle(nint pointer, int session)
    {
        Pointer = pointer;
        Session = session;
    }

    /// <summary>The engine controller the handle was minted for.</summary>
    internal nint Pointer { get; }

    /// <summary>The session the handle was minted in.</summary>
    internal int Session { get; }

    /// <summary>A handle that names no guest; the answer of a service that found none.</summary>
    public static GuestHandle None => default;

    /// <summary>Whether this handle names no guest at all, whatever session it is asked in.</summary>
    public bool IsNone => Pointer == 0;

    /// <summary>
    /// The projection of the group, or false when the session it was minted in has ended (or the group was
    /// never tracked). This is where a stale handle is caught.
    /// </summary>
    /// <param name="guest">The live projection, when the call answers true.</param>
    public bool TryGet([NotNullWhen(true)] out GuestProxy? guest) => GuestDirectory.TryResolve(this, out guest);

    public bool Equals(GuestHandle other) => Pointer == other.Pointer && Session == other.Session;

    public override bool Equals(object? obj) => obj is GuestHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Pointer, Session);

    public static bool operator ==(GuestHandle left, GuestHandle right) => left.Equals(right);

    public static bool operator !=(GuestHandle left, GuestHandle right) => !left.Equals(right);

    public override string ToString() => IsNone ? "guest (none)" : $"guest 0x{Pointer:x}@{Session}";
}

/// <summary>
/// What the framework answers about the guest group behind one <see cref="GuestHandle"/>. It is internal, so the
/// engine types the bridge implements it with never reach the public contract, and nothing outside the
/// framework can build a projection.
/// </summary>
internal interface IGuestEntity
{
    /// <summary>The pointer of the engine controller; half of the handle it is keyed by.</summary>
    nint Pointer { get; }

    /// <summary>The engine controller itself, for the framework's own bridge.</summary>
    object? Native { get; }

    int DeskCode { get; }

    GuestKind Kind { get; }

    /// <summary>How many characters belong to the group (one per seated guest).</summary>
    int GuestCount { get; }

    /// <summary>The ids of the guests of the group, in the group's own order.</summary>
    IReadOnlyList<int> GuestIds { get; }

    int Mood { get; }

    int Fund { get; }

    int MaxFundCarry { get; }

    int ExtraFundByBuff { get; }

    /// <summary>The endurance limit the game prices an order that outgrows the fund by.</summary>
    float EnduranceLimit { get; }

    /// <summary>Every order the group still holds, newest first (the game's own stack order).</summary>
    IReadOnlyList<OrderProxy> Orders { get; }

    GuestLeaveType FinalLeaveType { get; }

    /// <summary>Whether the group already received a verdict for its current order.</summary>
    bool HasEvaluated { get; }

    /// <summary>Whether the group left the izakaya (or is leaving).</summary>
    bool HasLeft { get; }

    /// <summary>Whether the group is waiting in the queue.</summary>
    bool IsQueued { get; }

    /// <summary>How many orders the group still holds; the top one is the pending order.</summary>
    int PendingOrderCount { get; }

    /// <summary>The order the group is currently considering, when it has one.</summary>
    bool TryGetPendingOrder([NotNullWhen(true)] out OrderProxy? order);

    void SetMood(int value);

    void SetFund(int value);

    void SetMaxFundCarry(int value);

    void SetPatience(int value);

    /// <summary>Marks the group as having left; the game reads it while it settles the leave.</summary>
    void MarkLeft();

    void MoveToQueue(Action? onArrived);

    void MoveToSpawn();

    void FlyToSpawn(bool instantly);

    void RemoveFromQueue();
}

/// <summary>
/// The read only projection of one guest group, with the actions the framework supports on it. It is only ever
/// built by the framework, and it is only valid while the session of its <see cref="Handle"/> is running —
/// a proxy a mod kept past the night its handle was minted in projects a controller the game already destroyed,
/// so a mod that wants to keep a group keeps the handle, not the proxy.
/// </summary>
public sealed class GuestProxy
{
    private readonly IGuestEntity _entity;

    internal GuestProxy(GuestHandle handle, IGuestEntity entity)
    {
        Handle = handle;
        _entity = entity;
    }

    /// <summary>The handle of this group; compare handles, not proxies.</summary>
    public GuestHandle Handle { get; }

    /// <summary>The desk the group sits at, or -1 while it has none.</summary>
    public int DeskCode => _entity.DeskCode;

    /// <summary>Whether the group is normal or special.</summary>
    public GuestKind Kind => _entity.Kind;

    /// <summary>How many characters belong to the group.</summary>
    public int GuestCount => _entity.GuestCount;

    /// <summary>The ids of the guests of the group.</summary>
    public IReadOnlyList<int> GuestIds => _entity.GuestIds;

    /// <summary>The mood the game will price the order by.</summary>
    public int Mood => _entity.Mood;

    /// <summary>What the group carries right now.</summary>
    public int Fund => _entity.Fund;

    /// <summary>What the group may carry at most.</summary>
    public int MaxFundCarry => _entity.MaxFundCarry;

    /// <summary>The extra fund a buff gave the group.</summary>
    public int ExtraFundByBuff => _entity.ExtraFundByBuff;

    /// <summary>The endurance limit the game prices an order that outgrows the fund by.</summary>
    public float EnduranceLimit => _entity.EnduranceLimit;

    /// <summary>Every order the group still holds, newest first; the pending one is <see cref="Orders"/>[0].</summary>
    public IReadOnlyList<OrderProxy> Orders => _entity.Orders;

    /// <summary>The leave type the game will settle the group with.</summary>
    public GuestLeaveType FinalLeaveType => _entity.FinalLeaveType;

    /// <summary>Whether the group already received a verdict for its current order.</summary>
    public bool HasEvaluated => _entity.HasEvaluated;

    /// <summary>Whether the group left the izakaya (or is leaving).</summary>
    public bool HasLeft => _entity.HasLeft;

    /// <summary>Whether the group is waiting in the queue.</summary>
    public bool IsQueued => _entity.IsQueued;

    /// <summary>How many orders the group still holds.</summary>
    public int PendingOrderCount => _entity.PendingOrderCount;

    /// <summary>The order the group is currently considering, or false when it has none.</summary>
    /// <param name="order">The pending order, when the call answers true.</param>
    public bool TryGetPendingOrder([NotNullWhen(true)] out OrderProxy? order) => _entity.TryGetPendingOrder(out order);

    /// <summary>Writes the mood the game prices by.</summary>
    public void SetMood(int value) => _entity.SetMood(value);

    /// <summary>Writes what the group carries.</summary>
    public void SetFund(int value) => _entity.SetFund(value);

    /// <summary>Writes what the group may carry.</summary>
    public void SetMaxFundCarry(int value) => _entity.SetMaxFundCarry(value);

    /// <summary>Writes the patience the group's countdown runs on.</summary>
    public void SetPatience(int value) => _entity.SetPatience(value);

    /// <summary>Marks the group as having left the izakaya.</summary>
    public void MarkLeft() => _entity.MarkLeft();

    /// <summary>Walks the group into the queue.</summary>
    /// <param name="onArrived">Run when the group arrived in the queue; null when the caller does not care.</param>
    public void MoveToQueue(Action? onArrived = null) => _entity.MoveToQueue(onArrived);

    /// <summary>Walks the group back to where it spawned.</summary>
    public void MoveToSpawn() => _entity.MoveToSpawn();

    /// <summary>Sends the group off the scene right away instead of walking it out.</summary>
    public void FlyToSpawn(bool instantly) => _entity.FlyToSpawn(instantly);

    /// <summary>Takes the group out of the queue.</summary>
    public void RemoveFromQueue() => _entity.RemoveFromQueue();

    /// <summary>The engine controller behind the projection, for the framework's own bridge.</summary>
    internal object? Native => _entity.Native;

    public override string ToString() => Handle.ToString();
}

/// <summary>
/// The projections minted this session, keyed by the pointer of the engine controller. The bridge fills it
/// (every listener notification, every service answer and every view read mints the group it names) and
/// <see cref="EntitySession.Rotate"/> empties it.
/// </summary>
internal static class GuestDirectory
{
    private static readonly Dictionary<nint, GuestProxy> Entries = [];

    private static Func<object, IGuestEntity?>? _factory;

    /// <summary>Installs the bridge's view of an engine guest group (it answers null for any other object).</summary>
    internal static void Install(Func<object, IGuestEntity?>? factory) => _factory = factory;

    /// <summary>The handle of one engine group, minted on first sight in this session.</summary>
    internal static GuestHandle Track(object? native)
    {
        if (native is null || _factory?.Invoke(native) is not { } entity)
            return default;
        if (Entries.TryGetValue(entity.Pointer, out var known))
            return known.Handle;

        var handle = new GuestHandle(entity.Pointer, EntitySession.Generation);
        Entries[entity.Pointer] = new GuestProxy(handle, entity);
        return handle;
    }

    /// <summary>The projection of one engine group, or null when the object is not a group.</summary>
    internal static GuestProxy? ProxyOf(object? native) => Track(native).TryGet(out var proxy) ? proxy : null;

    /// <summary>The projection a live handle names; false for a handle of a session that ended.</summary>
    internal static bool TryResolve(GuestHandle handle, [NotNullWhen(true)] out GuestProxy? guest)
    {
        if (handle.IsNone || handle.Session != EntitySession.Generation)
        {
            guest = null;
            return false;
        }

        return Entries.TryGetValue(handle.Pointer, out guest);
    }

    /// <summary>The engine group a live handle names, or null when the handle is stale.</summary>
    internal static object? NativeOf(GuestHandle handle) => TryResolve(handle, out var guest) ? guest.Native : null;

    internal static void Clear() => Entries.Clear();
}
