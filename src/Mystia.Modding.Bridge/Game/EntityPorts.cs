using System.Diagnostics.CodeAnalysis;
using Common.CharacterUtility;
using GameData.Core.Collections;
using GameData.Core.Collections.NightSceneUtility;
using Mystia.Scenes;
using Il2CppSystem.Linq;
using NightScene.GuestManagementUtility;

namespace Mystia.Modding.Bridge;

// The engine side of the three entity ports of Mystia.Scenes. Each port is the one place where the handle layer
// touches an engine object of its kind: the reads answer a value the SDK mirrors, the actions drive the game's
// own member, and Native hands the engine object back to the rest of the bridge (a view writing a slot, a
// service handing the object to a game method).
//
// Every member here is justified by a real read or write of the mod of this repository; the mapping is the one
// the bridge already used before the handles existed, so no behaviour changes with the projection.

/// <summary>The guest group behind one <see cref="GuestHandle"/>.</summary>
internal sealed class UnityGuestEntity : IGuestEntity
{
    private readonly GuestGroupController _group;

    internal UnityGuestEntity(GuestGroupController group) => _group = group;

    public nint Pointer => _group.Pointer;

    public object? Native => _group;

    public int DeskCode => _group.DeskCode;

    public GuestKind Kind => Mirrors.ToSdk(_group.ControllType);

    public int GuestCount => _group.GuestCount;

    public IReadOnlyList<int> GuestIds
    {
        get
        {
            // The group's own guests, in the group's own order: one for a special guest, one per seated normal
            // guest. GuestGroupController.GetAllGuests is what the mod reads the ids off (Managers/GuestFSM.cs:179).
            var ids = new List<int>(_group.GuestCount);
            // The interop enumerable of the guests of a group has no usable enumerator in this build, so the
            // array form is what the ids are read off (the mod does the same in Managers/GuestFSM.cs:179).
            foreach (var guest in _group.GetAllGuests().ToArray())
                ids.Add(guest.Id);

            return ids;
        }
    }

    public int Mood => _group.Mood;

    public int Fund => _group.GetFund;

    public int MaxFundCarry => _group.MaxFundCarry;

    public int ExtraFundByBuff => _group.ExtraFundByBuff;

    public GuestLeaveType FinalLeaveType => Mirrors.ToSdk(_group.FinalLeaveType);

    public bool HasEvaluated => _group.HasEvaluated;

    public bool HasLeft => !_group.HaveNotLeft();

    public bool IsQueued => _group.queued;

    public int PendingOrderCount => _group.AllOrdersCount;

    public bool TryGetPendingOrder([NotNullWhen(true)] out OrderProxy? order)
    {
        // PeekOrders on an empty stack is not a valid read (see IWorkSceneGuests.TryGetPendingOrder).
        order = PendingOrderCount > 0 ? OrderDirectory.ProxyOf(_group.PeekOrders()) : null;
        return order is not null;
    }

    public void SetMood(int value) => _group.Mood = value;

    public void SetFund(int value) => _group.GetFund = value;

    public void SetMaxFundCarry(int value) => _group.MaxFundCarry = value;

    public void SetPatience(int value) => _group.SetPatient(value);

    public void MarkLeft() => _group.Left = true;

    public void MoveToQueue(Action? onArrived) =>
        _group.MoveToQueue(
            (Il2CppSystem.Action<GuestGroupController>)(Action<GuestGroupController>)(_ => onArrived?.Invoke()),
            false);

    public void MoveToSpawn() => _group.MoveToSpawn();

    public void FlyToSpawn(bool instantly) => _group.FlyToSpawn(instantly);

    public void RemoveFromQueue() => _group.RemoveFromQueue();
}

/// <summary>The order behind one <see cref="OrderHandle"/>.</summary>
internal sealed class UnityOrderEntity : IOrderEntity
{
    private readonly GuestsManager.OrderBase _order;

    internal UnityOrderEntity(GuestsManager.OrderBase order) => _order = order;

    public nint Pointer => _order.Pointer;

    public object? Native => _order;

    public OrderKind Kind => Mirrors.ToSdk(_order.Type);

    public int DeskCode => _order.DeskCode;

    public int Price => _order.Price;

    public bool IsFulfilled => _order.IsFullfilled;

    public bool IsManual => _order.ManualOrder;

    public bool IsFree => _order.FreeOrder;

    public bool Hidden => _order.NotShowInUI;

    public int FoodRequest => _order.foodRequest;

    public int BeverageRequest => _order.beverageRequest;

    public DishProxy? Food => DishDirectory.ProxyOf(_order.ServFood);

    public DishProxy? Beverage => DishDirectory.ProxyOf(_order.ServBeverage);

    public DishProxy? FoodInAir => DishDirectory.ProxyOf(_order.ServedFoodInAir);

    public DishProxy? BeverageInAir => DishDirectory.ProxyOf(_order.ServedBeverageInAir);

    public void SetFood(DishProxy? dish) => _order.ServFood = Dish(dish);

    public void SetBeverage(DishProxy? dish) => _order.ServBeverage = Dish(dish);

    public void SetFoodInAir(DishProxy? dish) => _order.ServedFoodInAir = Dish(dish);

    public void SetBeverageInAir(DishProxy? dish) => _order.ServedBeverageInAir = Dish(dish);

    // A proxy whose dish a mod no longer holds (a stale one) clears the slot; the game reads null as "empty".
    private static Sellable? Dish(DishProxy? dish) => dish?.Native as Sellable;
}

/// <summary>The dish behind one <see cref="DishHandle"/>.</summary>
internal sealed class UnityDishEntity : IDishEntity
{
    private readonly Sellable _dish;

    internal UnityDishEntity(Sellable dish) => _dish = dish;

    public nint Pointer => _dish.Pointer;

    public object? Native => _dish;

    public DishKind Kind => Mirrors.ToSdk(_dish.Type);

    public int Id => _dish.Id;

    public int Level => _dish.Level;

    public IReadOnlyList<int> ModifierIds => Tags(_dish.Modifier);

    public IReadOnlyList<int> AdditiveTags => Tags(_dish.AdditiveTags);

    public string? Name => _dish.Text?.Name;

    public string? BriefName => _dish.Text?.BriefName;

    private static IReadOnlyList<int> Tags(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int>? tags)
    {
        if (tags is null)
            return [];
        var copy = new int[tags.Length];
        for (var index = 0; index < tags.Length; index++)
            copy[index] = tags[index];
        return copy;
    }

    private static IReadOnlyList<int> Tags(Il2CppSystem.Collections.Generic.List<int>? tags)
    {
        if (tags is null)
            return [];
        var copy = new int[tags.Count];
        for (var index = 0; index < tags.Count; index++)
            copy[index] = tags[index];
        return copy;
    }
}
