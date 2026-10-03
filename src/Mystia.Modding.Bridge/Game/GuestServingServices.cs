using GameData.Core.Collections;
using GameData.Core.Collections.NightSceneUtility;
using Mystia.Listeners;
using Mystia.Scenes;
using NightScene.GuestManagementUtility;
using NightScene.PartnerUtility;
using NightScene.Tiles;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The serving half of <see cref="IWorkSceneGuests"/>.
///
/// The scene's own guest services stay where they are: this class decorates them and answers the six
/// serving members, so the work loop still receives one <c>IWorkSceneGuests</c>.
/// Wire it inside <c>WorkSceneServices</c> as
/// <c>public IWorkSceneGuests Guests { get; } = WorkSceneGuestServing.Extend(new GuestServices());</c>.
/// </summary>
internal sealed class WorkSceneGuestServing : IWorkSceneGuests
{
    private readonly IWorkSceneGuests _inner;

    private WorkSceneGuestServing(IWorkSceneGuests inner) => _inner = inner;

    /// <summary>Wraps the scene's own guest services so the serving members land on the same object.</summary>
    internal static IWorkSceneGuests Extend(IWorkSceneGuests inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return new WorkSceneGuestServing(inner);
    }

    #region The members the scene's own guest services already answers

    public void SetSpawnEnabled(bool enabled) => _inner.SetSpawnEnabled(enabled);

    public void SetSeatingEnabled(bool enabled) => _inner.SetSeatingEnabled(enabled);

    public void SetLeaveEnabled(bool enabled) => _inner.SetLeaveEnabled(enabled);

    public void SetOrderingEnabled(bool enabled) => _inner.SetOrderingEnabled(enabled);

    public void SetEvaluationEnabled(bool enabled) => _inner.SetEvaluationEnabled(enabled);

    public GuestGroupController SpawnNormal(IReadOnlyList<NormalGuest> guests, int desk = -1) =>
        _inner.SpawnNormal(guests, desk);

    public GuestGroupController SpawnSpecial(int guestId, int desk = -1) => _inner.SpawnSpecial(guestId, desk);

    public bool Seat(GuestGroupController group, int desk, bool firstSpawn = true, int seat = -1) =>
        _inner.Seat(group, desk, firstSpawn, seat);

    public GuestGroupController At(int desk) => _inner.At(desk);

    public void Leave(GuestGroupController group, GuestLeaveKind kind) => _inner.Leave(group, kind);

    public void SetPatience(GuestGroupController group, int value) => _inner.SetPatience(group, value);

    public void BeginOrderSession(GuestGroupController group) => _inner.BeginOrderSession(group);

    public void BeginOrderSession(GuestGroupController group, GuestsManager.OrderBase order, string message) =>
        _inner.BeginOrderSession(group, order, message);

    public void BeginOrderSession(
        GuestGroupController group,
        GuestsManager.OrderGenerationResult result,
        GuestsManager.OrderBase order,
        string message) => _inner.BeginOrderSession(group, result, order, message);

    public void Evaluate(GuestGroupController group) => _inner.Evaluate(group);

    #endregion

    #region Serving

    public Sellable? ServeBeverage(GuestGroupController group, Sellable beverage)
    {
        ServiceScope.Require();
        Sellable? served = null;
        // GuestsManager.EvaluateOrder is the gated evaluation entry point (see SessionHolds); a beverage the
        // service itself serves must not be stopped by a switch the service is asked to respect elsewhere.
        StockGate.Bypass(() => served = Serve(group, beverage));
        return served;
    }

    public void SetBeverageInAir(GuestGroupController group, Sellable? beverage)
    {
        ServiceScope.Require();
        var order = Pending(group);
        if (order is null)
            return;
        order.ServedBeverageInAir = beverage;
    }

    public void NotifyOrderStatusUpdate(GuestsManager.OrderBase order, PartnerManager.OrderChangeContext context, int index)
    {
        ServiceScope.Require();
        PartnerManager.instance.OnOrderBaseStatusUpdate(order, context, index);
    }

    public IReadOnlyList<GuestGroupController> InDeskGuests
    {
        get
        {
            ServiceScope.Require();
            // AllGuestInDeskController is an Il2Cpp IEnumerable over the live desk table; copy it into a
            // managed list so the caller gets a stable, indexable view.
            var native = new Il2CppSystem.Collections.Generic.List<GuestGroupController>(
                GuestsManager.instance.AllGuestInDeskController);
            var guests = new List<GuestGroupController>(native.Count);
            for (var index = 0; index < native.Count; index++)
                guests.Add(native[index]);
            return guests;
        }
    }

    public GuestsManager.OrderBase? PendingOrder(GuestGroupController group)
    {
        ServiceScope.Require();
        return Pending(group);
    }

    public bool IsOrderFullfilled(GuestsManager.OrderBase order)
    {
        ServiceScope.Require();
        return order.IsFullfilled;
    }

    /// <summary>The order a group is still considering, or null when it has none (PeekOrders on an empty
    /// stack is not a valid read).</summary>
    private static GuestsManager.OrderBase? Pending(GuestGroupController? group) =>
        group is null || group.AllOrdersCount <= 0 ? null : group.PeekOrders();

    /// <summary>
    /// The game's own beverage delivery, step for step (the same sequence Spell_Mai reuses): the drink is
    /// registered on the order as being in the air and the partners are told at launch, then the order takes
    /// the beverage, the table gets its visual, and an order that is now full is evaluated.
    /// Returns the beverage that ended up on the order, or null when the serve was dropped.
    /// </summary>
    private static Sellable? Serve(GuestGroupController group, Sellable beverage)
    {
        if (group is null || beverage is null)
            return null;
        var order = Pending(group);
        if (order is null || order.ServBeverage is not null || order.ServedBeverageInAir is not null)
            return null;

        order.ServedBeverageInAir = beverage;
        // The original notifies as the drink leaves the hand, so a partner already walking food to this table
        // drops the job.
        PartnerManager.instance.OnOrderBaseStatusUpdate(
            order, PartnerManager.OrderChangeContext.BeverageDelivered, -1);

        order.ServBeverage = beverage;
        order.ServedBeverageInAir = null;
        var tables = TileManager.Instance.GuestTables;
        if (tables is not null && tables.ContainsKey(order.DeskCode))
        {
            var displayer = tables[order.DeskCode].tableDisplayer;
            if (displayer is not null)
                displayer.SetBeverageVisual(beverage.Text?.Visual);
        }

        if (order.IsFullfilled)
            GuestsManager.instance.EvaluateOrder(group, true, null);
        return order.ServBeverage;
    }

    #endregion
}
