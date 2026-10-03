using System.Runtime.CompilerServices;
using GameData.Core.Collections;
using Mystia.Listeners;
using Mystia.Scenes;
using NightScene.GuestManagementUtility;
using NightScene.PartnerUtility;

namespace Mystia.Modding.Bridge;

// The bridge's side of the SDK entity handles (see Mystia.Scenes.EntitySession for the shape and why the handle
// exists). Two jobs live here:
//
//   * the three ports the SDK's directories ask through (IGuestEntity/IOrderEntity/IDishEntity, implemented by
//     the classes at the bottom of this file) — they are the only place where an engine type of a guest, an
//     order or a dish is named for the handle layer;
//   * the enum mirrors of the values an entity carries across the contract, so the SDK never re-declares the
//     game's numbering and a change of it is caught here.
//
// The factories are installed when this assembly is loaded (the module initializer at the top of EntitySeams),
// which is before any scene can run, so a view or a listener that mints a handle always finds them.

internal static class EntitySeams
{
    [ModuleInitializer]
    internal static void Install()
    {
        GuestDirectory.Install(static native => native is GuestGroupController group ? new UnityGuestEntity(group) : null);
        OrderDirectory.Install(static native => native is GuestsManager.OrderBase order ? new UnityOrderEntity(order) : null);
        DishDirectory.Install(static native => native is Sellable dish ? new UnityDishEntity(dish) : null);
    }

    /// <summary>The handle of one engine group; the default handle when there is no group.</summary>
    internal static GuestHandle GuestHandleOf(GuestGroupController? group) => GuestDirectory.Track(group);

    /// <summary>The engine group a live handle names, or null when the handle is stale or names none.</summary>
    internal static GuestGroupController? GuestOf(GuestHandle handle) => GuestDirectory.NativeOf(handle) as GuestGroupController;

    /// <summary>The handle of one engine order; the default handle when there is no order.</summary>
    internal static OrderHandle OrderHandleOf(GuestsManager.OrderBase? order) => OrderDirectory.Track(order);

    /// <summary>The engine order a live handle names, or null when the handle is stale or names none.</summary>
    internal static GuestsManager.OrderBase? OrderOf(OrderHandle? handle) =>
        handle is { } value ? OrderDirectory.NativeOf(value) as GuestsManager.OrderBase : null;

    /// <summary>The engine dish a live handle names, or null when the handle is stale or names none.</summary>
    internal static Sellable? DishOf(DishHandle handle) => DishDirectory.NativeOf(handle) as Sellable;
}

/// <summary>The values that cross the entity contract, in the framework's own numbering.</summary>
internal static class Mirrors
{
    internal static GuestKind ToSdk(GuestsManager.GuestType kind) => kind switch
    {
        GuestsManager.GuestType.Special => GuestKind.Special,
        _ => GuestKind.Normal,
    };

    internal static GuestLeaveType ToSdk(GuestGroupController.LeaveType type) => type switch
    {
        GuestGroupController.LeaveType.Fading => GuestLeaveType.Fading,
        GuestGroupController.LeaveType.Delete => GuestLeaveType.Delete,
        GuestGroupController.LeaveType.MoveToTargetPosition => GuestLeaveType.MoveToTargetPosition,
        _ => GuestLeaveType.Move,
    };

    internal static GuestGroupController.LeaveType ToGame(GuestLeaveType type) => type switch
    {
        GuestLeaveType.Fading => GuestGroupController.LeaveType.Fading,
        GuestLeaveType.Delete => GuestGroupController.LeaveType.Delete,
        GuestLeaveType.MoveToTargetPosition => GuestGroupController.LeaveType.MoveToTargetPosition,
        _ => GuestGroupController.LeaveType.Move,
    };

    internal static GuestEvaluation ToSdk(GuestGroupController.EvaluationResult result) => result switch
    {
        GuestGroupController.EvaluationResult.Exbad => GuestEvaluation.ExBad,
        GuestGroupController.EvaluationResult.Bad => GuestEvaluation.Bad,
        GuestGroupController.EvaluationResult.Good => GuestEvaluation.Good,
        GuestGroupController.EvaluationResult.ExGood => GuestEvaluation.ExGood,
        GuestGroupController.EvaluationResult.Normal => GuestEvaluation.Normal,
        _ => GuestEvaluation.None,
    };

    internal static GuestGroupController.EvaluationResult ToGame(GuestEvaluation result) => result switch
    {
        GuestEvaluation.ExBad => GuestGroupController.EvaluationResult.Exbad,
        GuestEvaluation.Bad => GuestGroupController.EvaluationResult.Bad,
        GuestEvaluation.Good => GuestGroupController.EvaluationResult.Good,
        GuestEvaluation.ExGood => GuestGroupController.EvaluationResult.ExGood,
        GuestEvaluation.Normal => GuestGroupController.EvaluationResult.Normal,
        _ => GuestGroupController.EvaluationResult.Null,
    };

    internal static OrderKind ToSdk(GuestsManager.OrderBase.OrderType type) => type switch
    {
        GuestsManager.OrderBase.OrderType.Special => OrderKind.Special,
        _ => OrderKind.Normal,
    };

    internal static OrderGenerationOutcome ToSdk(GuestsManager.OrderGenerationResult result) => result switch
    {
        GuestsManager.OrderGenerationResult.OrderCountDepleted => OrderGenerationOutcome.OrderCountDepleted,
        GuestsManager.OrderGenerationResult.NoMoney => OrderGenerationOutcome.NoMoney,
        GuestsManager.OrderGenerationResult.ExceedEndurance => OrderGenerationOutcome.ExceedEndurance,
        GuestsManager.OrderGenerationResult.NotContinue => OrderGenerationOutcome.NotContinue,
        _ => OrderGenerationOutcome.Succeed,
    };

    internal static GuestsManager.OrderGenerationResult ToGame(OrderGenerationOutcome outcome) => outcome switch
    {
        OrderGenerationOutcome.OrderCountDepleted => GuestsManager.OrderGenerationResult.OrderCountDepleted,
        OrderGenerationOutcome.NoMoney => GuestsManager.OrderGenerationResult.NoMoney,
        OrderGenerationOutcome.ExceedEndurance => GuestsManager.OrderGenerationResult.ExceedEndurance,
        OrderGenerationOutcome.NotContinue => GuestsManager.OrderGenerationResult.NotContinue,
        _ => GuestsManager.OrderGenerationResult.Succeed,
    };

    internal static PartnerManager.OrderChangeContext ToGame(PartnerOrderContext context) => context switch
    {
        PartnerOrderContext.OrderAdd => PartnerManager.OrderChangeContext.OrderAdd,
        PartnerOrderContext.OrderRemove => PartnerManager.OrderChangeContext.OrderRemove,
        PartnerOrderContext.FoodDelivered => PartnerManager.OrderChangeContext.FoodDelivered,
        PartnerOrderContext.BeverageDelivered => PartnerManager.OrderChangeContext.BeverageDelivered,
        PartnerOrderContext.InventoryUpdate => PartnerManager.OrderChangeContext.InventoryUpdate,
        PartnerOrderContext.CookerStart => PartnerManager.OrderChangeContext.CookerStart,
        PartnerOrderContext.OnCookerAvailabilityUpdate => PartnerManager.OrderChangeContext.OnCookerAvailabilityUpdate,
        PartnerOrderContext.WakeUp => PartnerManager.OrderChangeContext.WakeUp,
        PartnerOrderContext.PlayerOccupyDesk => PartnerManager.OrderChangeContext.PlayerOccupyDesk,
        PartnerOrderContext.PartnerGetStuned => PartnerManager.OrderChangeContext.PartnerGetStuned,
        PartnerOrderContext.PartnerStunEnd => PartnerManager.OrderChangeContext.PartnerStunEnd,
        _ => PartnerManager.OrderChangeContext.Null,
    };

    internal static DishKind ToSdk(Sellable.SellableType type) => type switch
    {
        Sellable.SellableType.Beverage => DishKind.Beverage,
        _ => DishKind.Food,
    };
}
