using System.Diagnostics.CodeAnalysis;

namespace Mystia.Scenes;

// The order half of the entity handles (see EntitySession and GuestHandle for the shape and the reason).
//
// The audited reads and writes are the ones of the mod of this repository: Managers/GuestFSM.cs (the order
// state machine, which reads the desk, the served slots and the fulfilment of an order and writes the replayed
// dishes back onto it), Listeners/WorkSync.cs (which reads the served slots of the panel's order) and
// ResourceEx/SpellCollection/Spell_Mai.cs (which serves a beverage through the framework and re-checks the
// order it belongs to).

/// <summary>Whether an order was rolled for normal guests or for a special guest.</summary>
/// <remarks>Mirrors the game's <c>OrderBase.OrderType</c>, value for value.</remarks>
public enum OrderKind
{
    Normal = 0,
    Special = 1,
}

/// <summary>How the game's order roll ended.</summary>
/// <remarks>Mirrors the game's <c>GuestsManager.OrderGenerationResult</c>, value for value.</remarks>
public enum OrderGenerationOutcome
{
    Succeed = 0,
    OrderCountDepleted = 1,
    NoMoney = 2,
    ExceedEndurance = 3,
    NotContinue = 4,
}

/// <summary>What changed about an order, as the partners of the night are told it.</summary>
/// <remarks>Mirrors the game's <c>PartnerManager.OrderChangeContext</c>, value for value.</remarks>
public enum PartnerOrderContext
{
    None = 0,
    OrderAdd = 1,
    OrderRemove = 2,
    FoodDelivered = 3,
    BeverageDelivered = 4,
    InventoryUpdate = 5,
    CookerStart = 6,
    OnCookerAvailabilityUpdate = 7,
    WakeUp = 8,
    PlayerOccupyDesk = 9,
    PartnerGetStuned = 10,
    PartnerStunEnd = 11,
}

/// <summary>
/// An opaque order of the running scene; see <see cref="GuestHandle"/> for what a handle is and is not. An order
/// travels with the group it belongs to and stops resolving with it when the night ends.
/// </summary>
public readonly struct OrderHandle : IEquatable<OrderHandle>
{
    internal OrderHandle(nint pointer, int session)
    {
        Pointer = pointer;
        Session = session;
    }

    /// <summary>The engine order the handle was minted for.</summary>
    internal nint Pointer { get; }

    /// <summary>The session the handle was minted in.</summary>
    internal int Session { get; }

    /// <summary>A handle that names no order.</summary>
    public static OrderHandle None => default;

    /// <summary>Whether this handle names no order at all.</summary>
    public bool IsNone => Pointer == 0;

    /// <summary>The projection of the order, or false when the session it was minted in has ended.</summary>
    /// <param name="order">The live projection, when the call answers true.</param>
    public bool TryGet([NotNullWhen(true)] out OrderProxy? order) => OrderDirectory.TryResolve(this, out order);

    public bool Equals(OrderHandle other) => Pointer == other.Pointer && Session == other.Session;

    public override bool Equals(object? obj) => obj is OrderHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Pointer, Session);

    public static bool operator ==(OrderHandle left, OrderHandle right) => left.Equals(right);

    public static bool operator !=(OrderHandle left, OrderHandle right) => !left.Equals(right);

    public override string ToString() => IsNone ? "order (none)" : $"order 0x{Pointer:x}@{Session}";
}

/// <summary>What the framework answers about the order behind one <see cref="OrderHandle"/>.</summary>
internal interface IOrderEntity
{
    nint Pointer { get; }

    object? Native { get; }

    OrderKind Kind { get; }

    int DeskCode { get; }

    /// <summary>What the order pays.</summary>
    int Price { get; }

    /// <summary>Whether both the dish and the beverage of the order were served.</summary>
    bool IsFulfilled { get; }

    /// <summary>Whether the order was placed by the game's manual (driver or challenge) path.</summary>
    bool IsManual { get; }

    /// <summary>Whether the order costs the guest nothing.</summary>
    bool IsFree { get; }

    /// <summary>Whether the game keeps the order out of its order list UI.</summary>
    bool Hidden { get; }

    /// <summary>The food the order asks for, as a recipe id; -1 when it asks for none.</summary>
    int FoodRequest { get; }

    /// <summary>The beverage the order asks for, as a beverage id; -1 when it asks for none.</summary>
    int BeverageRequest { get; }

    /// <summary>The dish that was served on the food slot, or null while it is empty.</summary>
    DishProxy? Food { get; }

    /// <summary>The dish that was served on the beverage slot, or null while it is empty.</summary>
    DishProxy? Beverage { get; }

    /// <summary>The dish that is on its way to the food slot, or null when none is.</summary>
    DishProxy? FoodInAir { get; }

    /// <summary>The dish that is on its way to the beverage slot, or null when none is.</summary>
    DishProxy? BeverageInAir { get; }

    void SetFood(DishProxy? dish);

    void SetBeverage(DishProxy? dish);

    void SetFoodInAir(DishProxy? dish);

    void SetBeverageInAir(DishProxy? dish);
}

/// <summary>
/// The read only projection of one order, with the slot writes the framework supports on it. It carries the same
/// lifetime rule as every proxy: only while the session of its <see cref="Handle"/> runs.
/// </summary>
public sealed class OrderProxy
{
    private readonly IOrderEntity _entity;

    internal OrderProxy(OrderHandle handle, IOrderEntity entity)
    {
        Handle = handle;
        _entity = entity;
    }

    /// <summary>The handle of this order; compare handles, not proxies.</summary>
    public OrderHandle Handle { get; }

    /// <summary>Whether the order is a normal or a special guest's.</summary>
    public OrderKind Kind => _entity.Kind;

    /// <summary>The desk the order belongs to.</summary>
    public int DeskCode => _entity.DeskCode;

    /// <summary>What the order pays.</summary>
    public int Price => _entity.Price;

    /// <summary>Whether both the dish and the beverage of the order were served.</summary>
    public bool IsFulfilled => _entity.IsFulfilled;

    /// <summary>Whether the order was placed by the game's manual path.</summary>
    public bool IsManual => _entity.IsManual;

    /// <summary>Whether the order costs the guest nothing.</summary>
    public bool IsFree => _entity.IsFree;

    /// <summary>Whether the game keeps the order out of its order list UI.</summary>
    public bool Hidden => _entity.Hidden;

    /// <summary>The food the order asks for, as a recipe id; -1 when it asks for none.</summary>
    public int FoodRequest => _entity.FoodRequest;

    /// <summary>The beverage the order asks for, as a beverage id; -1 when it asks for none.</summary>
    public int BeverageRequest => _entity.BeverageRequest;

    /// <summary>The dish on the food slot, or null while the slot is empty.</summary>
    public DishProxy? Food => _entity.Food;

    /// <summary>The dish on the beverage slot, or null while the slot is empty.</summary>
    public DishProxy? Beverage => _entity.Beverage;

    /// <summary>The dish on its way to the food slot, or null when none is.</summary>
    public DishProxy? FoodInAir => _entity.FoodInAir;

    /// <summary>The dish on its way to the beverage slot, or null when none is.</summary>
    public DishProxy? BeverageInAir => _entity.BeverageInAir;

    /// <summary>Puts a dish on the food slot, or clears it with null.</summary>
    public void SetFood(DishProxy? dish) => _entity.SetFood(dish);

    /// <summary>Puts a dish on the beverage slot, or clears it with null.</summary>
    public void SetBeverage(DishProxy? dish) => _entity.SetBeverage(dish);

    /// <summary>Marks a dish as on its way to the food slot, or clears that mark with null.</summary>
    public void SetFoodInAir(DishProxy? dish) => _entity.SetFoodInAir(dish);

    /// <summary>Marks a dish as on its way to the beverage slot, or clears that mark with null.</summary>
    public void SetBeverageInAir(DishProxy? dish) => _entity.SetBeverageInAir(dish);

    /// <summary>The engine order behind the projection, for the framework's own bridge.</summary>
    internal object? Native => _entity.Native;

    public override string ToString() => Handle.ToString();
}

/// <summary>The order projections minted this session, keyed like <see cref="GuestDirectory"/>.</summary>
internal static class OrderDirectory
{
    private static readonly Dictionary<nint, OrderProxy> Entries = [];

    private static Func<object, IOrderEntity?>? _factory;

    /// <summary>Installs the bridge's view of an engine order (it answers null for any other object).</summary>
    internal static void Install(Func<object, IOrderEntity?>? factory) => _factory = factory;

    /// <summary>The handle of one engine order, minted on first sight in this session.</summary>
    internal static OrderHandle Track(object? native)
    {
        if (native is null || _factory?.Invoke(native) is not { } entity)
            return default;
        if (Entries.TryGetValue(entity.Pointer, out var known))
            return known.Handle;

        var handle = new OrderHandle(entity.Pointer, EntitySession.Generation);
        Entries[entity.Pointer] = new OrderProxy(handle, entity);
        return handle;
    }

    /// <summary>The projection of one engine order, or null when the object is not an order.</summary>
    internal static OrderProxy? ProxyOf(object? native) => Track(native).TryGet(out var proxy) ? proxy : null;

    /// <summary>The projection a live handle names; false for a handle of a session that ended.</summary>
    internal static bool TryResolve(OrderHandle handle, [NotNullWhen(true)] out OrderProxy? order)
    {
        if (handle.IsNone || handle.Session != EntitySession.Generation)
        {
            order = null;
            return false;
        }

        return Entries.TryGetValue(handle.Pointer, out order);
    }

    /// <summary>The engine order a live handle names, or null when the handle is stale.</summary>
    internal static object? NativeOf(OrderHandle handle) => TryResolve(handle, out var order) ? order.Native : null;

    internal static void Clear() => Entries.Clear();
}
