using System.Diagnostics.CodeAnalysis;
using GameData.Core.Collections;
using GameData.Core.Collections.CharacterUtility;
using GameData.Core.Collections.NightSceneUtility;
using Il2CppInterop.Runtime;
using Il2CppSystem.Linq;
using NightScene.GuestManagementUtility;
using NightScene.PartnerUtility;
using NightScene.Tiles;
using Mystia.Listeners;
using Mystia.Scenes;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The <see cref="IWorkSceneGuests"/> of the running work scene: the switches that gate the game's own guest
/// behaviour, the commands that drive one guest group, and the serving members.
///
/// Both halves live here — the scene's own guests and the serving decoration — so <c>WorkSceneServices</c> hands
/// the work loop exactly one object. The engine controller stays this file's business: every member of the
/// contract speaks handles and proxies (see <see cref="EntitySeams"/>), and a stale handle is refused instead of
/// reaching a controller the game destroyed.
/// </summary>
internal sealed class WorkSceneGuestServing : IWorkSceneGuests, IWorkSceneDishes
{
    /// <summary>The instance the work scene services hand out as <c>Guests</c>.</summary>
    internal static readonly WorkSceneGuestServing Shared = new();

    #region The switches

    public void SetSpawnEnabled(bool enabled)
    {
        ServiceScope.Require();
        NightScene.NightSceneDirector.instance.ShouldGuestSpawn(enabled);
    }

    public void SetSeatingEnabled(bool enabled)
    {
        ServiceScope.Require();
        StockGate.Seating = enabled;
    }

    public void SetLeaveEnabled(bool enabled)
    {
        ServiceScope.Require();
        StockGate.Leave = enabled;
    }

    public void SetOrderingEnabled(bool enabled)
    {
        ServiceScope.Require();
        StockGate.Order = enabled;
    }

    public void SetEvaluationEnabled(bool enabled)
    {
        ServiceScope.Require();
        StockGate.Evaluation = enabled;
    }

    #endregion

    #region Driving one group

    public GuestHandle SpawnNormal(IReadOnlyList<GuestDescription> guests, int desk = -1)
    {
        ServiceScope.Require();
        var resolved = GuestSpawnPipeline.Resolve(guests);
        var native = new Il2CppSystem.Collections.Generic.List<NormalGuest>(resolved.Count);
        foreach (var guest in resolved)
            native.Add(guest);
        var enumerable = (Il2CppSystem.Collections.Generic.IEnumerable<NormalGuest>)(object)native;
        var group = GuestsManager.instance.SpawnNormalGuestGroup(enumerable, default, GuestGroupController.LeaveType.Move, desk, true);
        return EntitySeams.GuestHandleOf(group);
    }

    public GuestHandle SpawnSpecial(int guestId, int desk = -1)
    {
        ServiceScope.Require();
        var group = GuestsManager.instance.SpawnSpecialGuestGroup(
            guestId,
            SpecialGuestsController.GuestSpawnType.Normal,
            default,
            null,
            GuestGroupController.LeaveType.Move,
            true,
            desk,
            false,
            null,
            true);
        return EntitySeams.GuestHandleOf(group);
    }

    public GuestHandle SpawnNormal(IReadOnlyList<GuestDescription> guests, GuestSpawnRequest request)
    {
        ServiceScope.Require();
        var resolved = GuestSpawnPipeline.Resolve(guests);
        var native = new Il2CppSystem.Collections.Generic.List<NormalGuest>(resolved.Count);
        foreach (var guest in resolved)
            native.Add(guest);
        var enumerable = (Il2CppSystem.Collections.Generic.IEnumerable<NormalGuest>)(object)native;
        var group = GuestsManager.instance.SpawnNormalGuestGroup(
            enumerable,
            Position(request.SpawnPosition),
            Mirrors.ToGame(request.LeaveType),
            request.DeskCode,
            request.Fade);
        return EntitySeams.GuestHandleOf(group);
    }

    public GuestHandle SpawnSpecial(int guestId, GuestSpawnRequest request)
    {
        ServiceScope.Require();
        var group = GuestsManager.instance.SpawnSpecialGuestGroup(
            guestId,
            SpecialGuestsController.GuestSpawnType.Normal,
            Position(request.SpawnPosition),
            null,
            Mirrors.ToGame(request.LeaveType),
            true,
            request.DeskCode,
            false,
            // The game's own character post-processing is what its own spawn path hands the controller (the mod
            // that replayed this spawn used the same callback).
            GuestsManager.instance.getPostprocessCharacterCallback.Invoke(),
            request.Fade);
        return EntitySeams.GuestHandleOf(group);
    }

    /// <summary>The mirrored spawn position as the game's own nullable vector.</summary>
    private static Il2CppSystem.Nullable<UnityEngine.Vector3> Position(Mystia.Numerics.Vector3? position) =>
        position is { } value
            ? new Il2CppSystem.Nullable<UnityEngine.Vector3>(new UnityEngine.Vector3(value.X, value.Y, value.Z))
            : new Il2CppSystem.Nullable<UnityEngine.Vector3>();

    public bool Seat(GuestHandle group, int desk, bool firstSpawn = true, int seat = -1)
    {
        ServiceScope.Require();
        if (EntitySeams.GuestOf(group) is not { } native)
            return false;
        if (seat >= 0)
            SeatChoice.Remember(native, seat);
        var seated = false;
        StockGate.Bypass(() => seated = GuestsManager.instance.TrySendToSeat(native, firstSpawn, desk, true));
        return seated;
    }

    public bool TryGetSeated(int desk, [NotNullWhen(true)] out GuestProxy? guest)
    {
        ServiceScope.Require();
        guest = GuestDirectory.ProxyOf(GuestsManager.instance.GetInDeskGuest(desk));
        return guest is not null;
    }

    public void Leave(GuestHandle group, GuestLeaveKind kind)
    {
        ServiceScope.Require();
        if (EntitySeams.GuestOf(group) is not { } native)
            return;
        StockGate.Bypass(() => LeaveNow(native, kind));
    }

    public void SetPatience(GuestHandle group, int value)
    {
        ServiceScope.Require();
        EntitySeams.GuestOf(group)?.SetPatient(value);
    }

    public void BeginOrderSession(GuestHandle group)
    {
        ServiceScope.Require();
        if (EntitySeams.GuestOf(group) is not { } native)
            return;
        StockGate.Bypass(() => GameMembers.Invoke(GuestsManager.instance, "GenerateOrderSession", native, true));
    }

    public void BeginOrderSession(GuestHandle group, OrderHandle order, string message)
    {
        ServiceScope.Require();
        if (EntitySeams.GuestOf(group) is not { } native)
            return;
        if (EntitySeams.OrderOf(order) is not { } orderData)
            return;
        PendingOrder.Arm(native, orderData, message);
        GameMembers.Invoke(GuestsManager.instance, "GenerateOrderSession", native, true);
    }

    public void BeginOrderSession(GuestHandle group, OrderGenerationOutcome result, OrderHandle order, string message)
    {
        ServiceScope.Require();
        if (EntitySeams.GuestOf(group) is not { } native)
            return;
        if (EntitySeams.OrderOf(order) is not { } orderData)
            return;
        PendingOrder.Arm(native, orderData, message);
        // The order hook drops the pending order as soon as the game takes it, so the result is kept
        // next to it for the whole session (see OrderHolds); the legacy overload arms no result.
        PendingOrderResult.Arm(native, Mirrors.ToGame(result));
        GameMembers.Invoke(GuestsManager.instance, "GenerateOrderSession", native, true);
    }

    public void Evaluate(GuestHandle group)
    {
        ServiceScope.Require();
        if (EntitySeams.GuestOf(group) is not { } native)
            return;
        // EvaluateOrder carries the SetEvaluationEnabled gate; the service's own evaluation bypasses it.
        StockGate.Bypass(() => GuestsManager.instance.EvaluateOrder(native, false, null));
    }

    public bool CanQueue(GuestHandle group)
    {
        ServiceScope.Require();
        return EntitySeams.GuestOf(group) is { } native && GuestGroupController.CanQueue(native.GuestCount);
    }

    public void BeginManualOrder(GuestHandle group, OrderHandle order, Action<GuestEvaluation> onEvaluated)
    {
        ServiceScope.Require();
        if (EntitySeams.GuestOf(group) is not { } native || EntitySeams.OrderOf(order) is not { } orderData)
            return;

        var callback = Verdict(onEvaluated);
        StockGate.Bypass(() => GuestsManager.instance.SetManualControllerOrderInternal(native, callback, orderData));
    }

    public void EvaluateManual(GuestHandle group, Action<GuestEvaluation> onEvaluated)
    {
        ServiceScope.Require();
        if (EntitySeams.GuestOf(group) is not { } native)
            return;

        var callback = Verdict(onEvaluated);
        StockGate.Bypass(() => GuestsManager.instance.EvaulateManualOrder(native, callback));
    }

    public void SetEvaluated(GuestHandle group)
    {
        ServiceScope.Require();
        if (EntitySeams.GuestOf(group) is { } native)
            native.HasEvaluated = true;
    }

    public void CleanOrderInfo(GuestHandle group)
    {
        ServiceScope.Require();
        if (EntitySeams.GuestOf(group) is { } native)
            GuestsManager.instance.CleanOrderInfo(native);
    }

    /// <summary>The game's own verdict callback, handed to the manual order path in the entity layer's numbering.</summary>
    private static Il2CppSystem.Action<GuestGroupController.EvaluationResult> Verdict(Action<GuestEvaluation> onEvaluated) =>
        (Il2CppSystem.Action<GuestGroupController.EvaluationResult>)(Action<GuestGroupController.EvaluationResult>)(
            result => onEvaluated(Mirrors.ToSdk(result)));

    public OrderHandle CreateOrder(GuestHandle group, OrderKind kind, int foodRequest, int beverageRequest, int deskCode, bool hidden, bool free)
    {
        ServiceScope.Require();
        if (EntitySeams.GuestOf(group) is not { } native)
            return OrderHandle.None;

        // The game's own two order kinds, built the way its own generation builds them
        // (GuestsManager.GenerateOrderInternal): a normal order carries the group's first guest, a special
        // order the special guest the group was spawned with.
        if (kind == OrderKind.Normal)
        {
            GuestBase? first = null;
            foreach (var guest in native.GetAllGuests().ToArray())
            {
                first = guest;
                break;
            }

            return first is null
                ? OrderHandle.None
                : EntitySeams.OrderHandleOf(new GuestsManager.NormalOrder(first, foodRequest, beverageRequest, deskCode, hidden, free));
        }

        var specialId = GuestDirectory.ProxyOf(native)?.GuestIds.FirstOrDefault() ?? -1;
        return specialId < 0
            ? OrderHandle.None
            : EntitySeams.OrderHandleOf(new GuestsManager.SpecialOrder(specialId.RefSGuest(), foodRequest, beverageRequest, deskCode, hidden, free));
    }

    public bool TryQueue(GuestHandle group, bool tryToJumpQueue = false)
    {
        ServiceScope.Require();
        if (EntitySeams.GuestOf(group) is not { } native)
            return false;
        if (!GuestGroupController.CanQueue(native.GuestCount))
            return false;

        // The game's own queue branch (GuestsManager.PostInitializeGuestGroup), step for step, except for the
        // verdict: the walk into a waiting seat, the countdown armed when the walk ends, the registration with
        // the manager. The armed countdown callback does nothing when the patience runs out — a replayed queue
        // entry is the host's verdict to give, not the replaying machine's — and the listeners hear the
        // depletion from the countdown seam (see GuestSeams.QueueCountdown) either way.
        native.MoveToQueue(
            (Il2CppSystem.Action<GuestGroupController>)(Action<GuestGroupController>)(arrived =>
                GuestsManager.instance.AddToPatientCountdown(
                    arrived,
                    (Il2CppSystem.Action<GuestGroupController>)(Action<GuestGroupController>)(static _ => { }))),
            tryToJumpQueue);
        GuestsManager.instance.SpawnGuest(native);
        return true;
    }

    public void StopPatientCountdown(GuestHandle group)
    {
        ServiceScope.Require();
        if (EntitySeams.GuestOf(group) is { } native)
            GuestsManager.instance.RemoveFromPatientCountdown(native);
    }

    public void ShowMood(GuestHandle group)
    {
        ServiceScope.Require();
        if (EntitySeams.GuestOf(group) is not { } native)
            return;
        if (Table(native.DeskCode) is not { } table)
            return;

        // The game's own first order display (GuestsManager.FirstOrder): the bar appears, the group's mood
        // updates drive it, and re-setting the mood pushes the first value through that callback.
        table.ShowMood();
        native.OnMoodUpdateCallback += DelegateSupport.ConvertDelegate<Il2CppSystem.Action<float>>(
            (Action<float>)table.SetMoodProgress);
        native.Mood = native.Mood;
    }

    public void SetRepellable(GuestHandle group)
    {
        ServiceScope.Require();
        if (EntitySeams.GuestOf(group) is { } native)
            GuestsManager.instance.SetPlayerCanRepelGuest(native);
    }

    public void ShowServedDish(int deskCode, DishProxy? dish, DishKind kind)
    {
        ServiceScope.Require();
        if (Table(deskCode) is not { } table)
            return;

        // A cleared slot keeps the other one: the game renderer takes the sprite of the other slot from the
        // renderer itself (GuestTableDisplayer.SetFoodVisual/SetBeverageVisual).
        var visual = (dish?.Native as Sellable)?.Text?.Visual;
        if (kind == DishKind.Food)
            table.SetFoodVisual(visual);
        else
            table.SetBeverageVisual(visual);
    }

    /// <summary>The displayer of one desk's table, or null when the running scene has no such table.</summary>
    private static GuestTableDisplayer? Table(int deskCode)
    {
        var tables = TileManager.Instance.GuestTables;
        return tables is not null && tables.ContainsKey(deskCode) ? tables[deskCode].tableDisplayer : null;
    }

    private static void LeaveNow(GuestGroupController group, GuestLeaveKind kind)
    {
        var manager = GuestsManager.instance;
        switch (kind)
        {
            case GuestLeaveKind.Paid:
                manager.PayAndLeave(group, true);
                break;
            case GuestLeaveKind.ExBad:
                GameMembers.Invoke(manager, "ExBadLeave", group);
                break;
            case GuestLeaveKind.RepelledPaid:
                manager.RepellAndLeavePay(group, GuestGroupController.LeaveType.Move, true);
                break;
            case GuestLeaveKind.RepelledUnpaid:
                manager.RepellAndLeaveNoPay(group, GuestGroupController.LeaveType.Move, true);
                break;
            case GuestLeaveKind.PlayerRepelled:
                manager.PlayerRepell(group.DeskCode);
                break;
            case GuestLeaveKind.Patience:
                GameMembers.Invoke(manager, "PatientDepletedLeave", group);
                break;
            case GuestLeaveKind.Other:
                GameMembers.Invoke(manager, "LeaveFromDesk", group, GuestGroupController.LeaveType.Move, null, true);
                break;
        }
    }

    #endregion

    #region The dish boundary

    public DishProxy? DishOf(Sellable dish) => DishDirectory.ProxyOf(dish);

    #endregion

    #region Serving

    public DishProxy? ServeBeverage(GuestHandle group, DishProxy beverage)
    {
        ServiceScope.Require();
        if (EntitySeams.GuestOf(group) is not { } native || beverage.Native is not Sellable dish)
            return null;
        Sellable? served = null;
        // GuestsManager.EvaluateOrder is the gated evaluation entry point (see SessionHolds); a beverage the
        // service itself serves must not be stopped by a switch the service is asked to respect elsewhere.
        StockGate.Bypass(() => served = Serve(native, dish));
        return DishDirectory.ProxyOf(served);
    }

    public void SetBeverageInAir(GuestHandle group, DishProxy? beverage)
    {
        ServiceScope.Require();
        if (EntitySeams.GuestOf(group) is not { } native)
            return;
        if (Pending(native) is not { } order)
            return;
        order.ServedBeverageInAir = beverage?.Native as Sellable;
    }

    public void NotifyOrderStatusUpdate(OrderHandle order, PartnerOrderContext context, int index)
    {
        ServiceScope.Require();
        if (EntitySeams.OrderOf(order) is not { } native)
            return;
        PartnerManager.instance.OnOrderBaseStatusUpdate(native, Mirrors.ToGame(context), index);
    }

    public IReadOnlyList<GuestHandle> InDeskGuests
    {
        get
        {
            ServiceScope.Require();
            // AllGuestInDeskController is an Il2Cpp IEnumerable over the live desk table; copy it into a
            // managed list so the caller gets a stable, indexable view, and hand out handles rather than the
            // controllers (a stale handle is refused, a kept controller is not).
            var native = new Il2CppSystem.Collections.Generic.List<GuestGroupController>(
                GuestsManager.instance.AllGuestInDeskController);
            var guests = new List<GuestHandle>(native.Count);
            for (var index = 0; index < native.Count; index++)
                guests.Add(EntitySeams.GuestHandleOf(native[index]));
            return guests;
        }
    }

    public bool TryGetPendingOrder(GuestHandle group, [NotNullWhen(true)] out OrderProxy? order)
    {
        ServiceScope.Require();
        order = EntitySeams.GuestOf(group) is { } native ? OrderDirectory.ProxyOf(Pending(native)) : null;
        return order is not null;
    }

    public bool IsOrderFullfilled(OrderHandle order)
    {
        ServiceScope.Require();
        return EntitySeams.OrderOf(order) is { IsFullfilled: true };
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
