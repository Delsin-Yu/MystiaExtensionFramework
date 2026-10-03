using System.Diagnostics.CodeAnalysis;

namespace Mystia.Scenes;

// The dish half of the entity handles (see EntitySession and GuestHandle for the shape and the reason).
//
// A dish is the game's Sellable: the food and the beverage a guest is served, the thing on the tray and in the
// storage box. The audited reads are the ones the mod of this repository sends over its own wire
// (Multiplayer/Messages/WorkScene/SellableFood.cs: the kind, the id, the level, the modifiers, the additive tags
// and the display name) and the two the serving panel writes (its pending food and pending beverage slots).

/// <summary>Whether a dish is food or a beverage.</summary>
/// <remarks>Mirrors the game's <c>Sellable.SellableType</c>, value for value.</remarks>
public enum DishKind
{
    Food = 0,
    Beverage = 1,
}

/// <summary>
/// An opaque dish of the running scene; see <see cref="GuestHandle"/> for what a handle is and is not. A dish
/// belongs to the night it was made in: a plate the storage box still holds after a night ends is a new dish of
/// the next night, and the handle of the old one stops resolving.
/// </summary>
public readonly struct DishHandle : IEquatable<DishHandle>
{
    internal DishHandle(nint pointer, int session)
    {
        Pointer = pointer;
        Session = session;
    }

    /// <summary>The engine dish the handle was minted for.</summary>
    internal nint Pointer { get; }

    /// <summary>The session the handle was minted in.</summary>
    internal int Session { get; }

    /// <summary>A handle that names no dish.</summary>
    public static DishHandle None => default;

    /// <summary>Whether this handle names no dish at all.</summary>
    public bool IsNone => Pointer == 0;

    /// <summary>The projection of the dish, or false when the session it was minted in has ended.</summary>
    /// <param name="dish">The live projection, when the call answers true.</param>
    public bool TryGet([NotNullWhen(true)] out DishProxy? dish) => DishDirectory.TryResolve(this, out dish);

    public bool Equals(DishHandle other) => Pointer == other.Pointer && Session == other.Session;

    public override bool Equals(object? obj) => obj is DishHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Pointer, Session);

    public static bool operator ==(DishHandle left, DishHandle right) => left.Equals(right);

    public static bool operator !=(DishHandle left, DishHandle right) => !left.Equals(right);

    public override string ToString() => IsNone ? "dish (none)" : $"dish 0x{Pointer:x}@{Session}";
}

/// <summary>What the framework answers about the dish behind one <see cref="DishHandle"/>.</summary>
internal interface IDishEntity
{
    nint Pointer { get; }

    object? Native { get; }

    DishKind Kind { get; }

    /// <summary>The id of the dish in the game's own database (the recipe or the beverage id).</summary>
    int Id { get; }

    /// <summary>The level a food was cooked at; a beverage carries zero.</summary>
    int Level { get; }

    /// <summary>The ids of the ingredients a food was cooked with.</summary>
    IReadOnlyList<int> ModifierIds { get; }

    /// <summary>The tags the dish added to itself (the seasoned tags).</summary>
    IReadOnlyList<int> AdditiveTags { get; }

    /// <summary>The full name the game shows for the dish, or null when the language data is missing.</summary>
    string? Name { get; }

    /// <summary>The short name the game shows for the dish, or null when the language data is missing.</summary>
    string? BriefName { get; }
}

/// <summary>
/// The read only projection of one dish. It carries the same lifetime rule as every proxy: only while the
/// session of its <see cref="Handle"/> runs.
/// </summary>
public sealed class DishProxy
{
    private readonly IDishEntity _entity;

    internal DishProxy(DishHandle handle, IDishEntity entity)
    {
        Handle = handle;
        _entity = entity;
    }

    /// <summary>The handle of this dish; compare handles, not proxies.</summary>
    public DishHandle Handle { get; }

    /// <summary>Whether the dish is food or a beverage.</summary>
    public DishKind Kind => _entity.Kind;

    /// <summary>The id of the dish in the game's own database.</summary>
    public int Id => _entity.Id;

    /// <summary>The level a food was cooked at; a beverage carries zero.</summary>
    public int Level => _entity.Level;

    /// <summary>The ids of the ingredients a food was cooked with.</summary>
    public IReadOnlyList<int> ModifierIds => _entity.ModifierIds;

    /// <summary>The tags the dish added to itself.</summary>
    public IReadOnlyList<int> AdditiveTags => _entity.AdditiveTags;

    /// <summary>The full name the game shows for the dish, or null when the language data is missing.</summary>
    public string? Name => _entity.Name;

    /// <summary>The short name the game shows for the dish, or null when the language data is missing.</summary>
    public string? BriefName => _entity.BriefName;

    /// <summary>The engine dish behind the projection, for the framework's own bridge.</summary>
    internal object? Native => _entity.Native;

    public override string ToString() => $"{Handle} ({_entity.Kind} {_entity.Id})";
}

/// <summary>The dish projections minted this session, keyed like <see cref="GuestDirectory"/>.</summary>
internal static class DishDirectory
{
    private static readonly Dictionary<nint, DishProxy> Entries = [];

    private static Func<object, IDishEntity?>? _factory;

    /// <summary>Installs the bridge's view of an engine dish (it answers null for any other object).</summary>
    internal static void Install(Func<object, IDishEntity?>? factory) => _factory = factory;

    /// <summary>The handle of one engine dish, minted on first sight in this session.</summary>
    internal static DishHandle Track(object? native)
    {
        if (native is null || _factory?.Invoke(native) is not { } entity)
            return default;
        if (Entries.TryGetValue(entity.Pointer, out var known))
            return known.Handle;

        var handle = new DishHandle(entity.Pointer, EntitySession.Generation);
        Entries[entity.Pointer] = new DishProxy(handle, entity);
        return handle;
    }

    /// <summary>The projection of one engine dish, or null when the object is not a dish.</summary>
    internal static DishProxy? ProxyOf(object? native) => Track(native).TryGet(out var proxy) ? proxy : null;

    /// <summary>The projection a live handle names; false for a handle of a session that ended.</summary>
    internal static bool TryResolve(DishHandle handle, [NotNullWhen(true)] out DishProxy? dish)
    {
        if (handle.IsNone || handle.Session != EntitySession.Generation)
        {
            dish = null;
            return false;
        }

        return Entries.TryGetValue(handle.Pointer, out dish);
    }

    /// <summary>The engine dish a live handle names, or null when the handle is stale.</summary>
    internal static object? NativeOf(DishHandle handle) => TryResolve(handle, out var dish) ? dish.Native : null;

    internal static void Clear() => Entries.Clear();
}
