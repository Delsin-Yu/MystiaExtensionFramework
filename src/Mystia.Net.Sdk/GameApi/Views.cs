using Common.UI;
using Common.UI.GlobalMap;
using GameData.Core.Collections;
using NightScene.GuestManagementUtility;
using NightScene.UI.CookingUtility;
using NightScene.UI.GuestManagementUtility;
using PrepNightScene.UI;

namespace Mystia.Scenes;

// Views are the framework's own types: the game has no such classes. The bridge builds one from the game
// panel it is sitting in (the internal constructor) and hands it to listeners in place of the panel itself.
// A view of a panel is only valid while that panel is open, so a mod must not keep one past the callback that
// gave it; ServeCallbackView is the exception and stays valid for the whole opening it stands for.

/// <summary>The work scene serve panel of one desk.</summary>
/// <remarks>
/// The panel's guest, order and two pending slots are handed out as the framework's own entities
/// (<see cref="GuestProxy"/>, <see cref="OrderProxy"/>, <see cref="DishProxy"/>), never as the game's
/// controller, order or sellable, so a mod that acts on the panel names no game type to do it. The handles of
/// those proxies are minted for the night the panel is open in, which is also how long the panel itself lives.
/// </remarks>
public sealed class ServePannelView
{
    private readonly WorkSceneServePannel _panel;

    internal ServePannelView(WorkSceneServePannel panel) => _panel = panel;

    /// <summary>The guest group the panel serves; null while the panel has no guest.</summary>
    public GuestProxy? Guest => GuestDirectory.ProxyOf(_panel.currentGuestController);

    /// <summary>The order the panel serves; null while the panel has no order.</summary>
    public OrderProxy? Order => OrderDirectory.ProxyOf(_panel.operatingOrder);

    /// <summary>The desk the panel serves, or -1 when the panel has neither guest nor order.</summary>
    public int DeskCode => _panel.currentGuestController?.DeskCode ?? _panel.operatingOrder?.DeskCode ?? -1;

    /// <summary>The dish taken off the tray and waiting for confirmation; null means none.</summary>
    public DishProxy? PendingFood
    {
        get => DishDirectory.ProxyOf(_panel.willServeFood);
        set => _panel.willServeFood = (Sellable?)value?.Native;
    }

    /// <summary>The beverage taken off the tray and waiting for confirmation; null means none.</summary>
    public DishProxy? PendingBeverage
    {
        get => DishDirectory.ProxyOf(_panel.willServeBeverage);
        set => _panel.willServeBeverage = (Sellable?)value?.Native;
    }

    /// <summary>Renders both pending slots: a filled slot shows its icon as cancellable, an empty one is cleared.</summary>
    public void RefreshPendingVisual()
    {
        var food = (Sellable?)PendingFood?.Native;
        var beverage = (Sellable?)PendingBeverage?.Native;
        if (food is null)
            _panel.ResetServedVisualOnUI(_panel.servFood, _panel.servFoodOutline);
        else
            _panel.SetServedVisualOnUI(_panel.servFood, _panel.servFoodOutline, food, true);

        if (beverage is null)
            _panel.ResetServedVisualOnUI(_panel.servBev, _panel.servBevOutline);
        else
            _panel.SetServedVisualOnUI(_panel.servBev, _panel.servBevOutline, beverage, true);
    }

    /// <summary>Drops both pending slots and clears their visuals.</summary>
    public void ResetPendingVisual()
    {
        PendingFood = null;
        PendingBeverage = null;
        _panel.ResetServedVisualOnUI(_panel.servFood, _panel.servFoodOutline);
        _panel.ResetServedVisualOnUI(_panel.servBev, _panel.servBevOutline);
    }

    /// <summary>Closes the panel through the game's own close path.</summary>
    public void Close() => _panel.CloseExternPanel();
}

/// <summary>The izakaya selection guide map.</summary>
public sealed class GuideMapView
{
    private readonly IzakayaSelectorPanel_New _panel;

    internal GuideMapView(IzakayaSelectorPanel_New panel) => _panel = panel;

    /// <summary>
    /// The spot the player currently points at; null when there is none. This is the same object the game
    /// maps to the panel's spot extension, so it is what <see cref="IPrepNightMapServices.Confirm"/> needs.
    /// </summary>
    public IGuideMapSpot? SelectedSpot => _panel.m_CurrentSelectedSpot;

    /// <summary>Label of the spot the player currently points at; null when there is none.</summary>
    public string? SelectedMapLabel => _panel.m_CurrentSelectedSpot?.PrimaryName;

    /// <summary>The selected izakaya level as the game's <c>IzakayaLevel</c> value; 0 means not selected.</summary>
    public int SelectedLevel => (int)_panel.m_CurrentSelectedIzakayaLevel;

    /// <summary>Re-reads the current selection into the describer and the level toggles.</summary>
    public void RefreshSelection() => _panel.UpdateCurrentIzakaya();
}

/// <summary>The prep night izakaya configuration panel.</summary>
public sealed class PrepConfigView
{
    private readonly IzakayaConfigPannel _panel;

    internal PrepConfigView(IzakayaConfigPannel panel) => _panel = panel;

    /// <summary>Whether the panel is currently open.</summary>
    public bool IsOpen => _panel.IsPanelOpened;

    /// <summary>
    /// The name of the panel object. The game keys its panel stack by that name, so a caller that wants to
    /// close the panels stacked above this one passes it to the game's own close-until helper.
    /// </summary>
    public string Name => _panel.name;

    /// <summary>The open tab: 0 recipe, 1 beverage, 2 cooker (the game's <c>CurrentConfigType</c>).</summary>
    public int SelectedTab => (int)_panel.m_CurrentConfigType;

    /// <summary>Refreshes the completion state (work button and blockers).</summary>
    public void Refresh() => _panel.SolveDailyCompletion();

    /// <summary>Rebuilds the recipe, beverage and cooker lists from the current configuration.</summary>
    public void UpdateGroups()
    {
        _panel.m_RecipeGroup?.UpdateGroupRaw();
        _panel.m_BeverageGroup?.UpdateGroupRaw();
        _panel.m_CookerGroup?.UpdateGroupRaw();
    }

    public void UpdateCookers() => _panel.m_CookerGroup?.UpdateGroupRaw();

    /// <summary>Refreshes the whole panel: completion state plus the three lists.</summary>
    public void UpdateUi()
    {
        Refresh();
        UpdateGroups();
    }

    /// <summary>
    /// Closes the panel through the game's own close path and hands out the token the close fade cancels when
    /// it finishes, so the caller can wait for the panel to be fully gone. The token has to be read before the
    /// close starts: the game replaces it for every open/close cycle.
    /// </summary>
    public Il2CppSystem.Threading.CancellationToken CloseWithFadeToken()
    {
        var fade = _panel.OnPanelCloseFadeFinishToken;
        _panel.ClosePanel();
        return fade;
    }
}

/// <summary>Which deferred callback of one serve panel opening is about to run.</summary>
public enum ServeCallbackKind
{
    /// <summary>The order was fulfilled and the game is about to evaluate it.</summary>
    OrderEvaluate,

    /// <summary>The order was not filled and the guest is about to recover patience instead.</summary>
    PatientRecover,

    /// <summary>The food of the order is about to be shown on the desk.</summary>
    FoodDeliverStatusUpdated,

    /// <summary>The beverage of the order is about to be shown on the desk.</summary>
    BeverageDeliverStatusUpdated,
}

/// <summary>
/// The four callbacks one <c>WorkSceneSustainedPannel.OpenServePanel</c> call registers, with the order they
/// were opened for.
/// <para>
/// The serve panel runs them after it opened, and in throw deliver mode it copies them into its throw routine
/// and invokes them once the animation lands — which can be later than the order they were opened for. Such a
/// stale callback then evaluates the desk's next order or writes the previous dish onto it.
/// </para>
/// <para>
/// One view belongs to one opening, never to the panel: the panel opens again for the next order while the
/// callbacks of the previous one may still be in flight, so the two openings must not share a view. A listener is
/// handed the view when the callbacks are registered (its chance to remember the order's state) and again before
/// each one runs, where it may drop the ones whose order moved on.
/// </para>
/// </summary>
public sealed class ServeCallbackView
{
    internal ServeCallbackView(OrderProxy order, GuestProxy? guest)
    {
        Order = order;
        Guest = guest;
    }

    /// <summary>The order the callbacks were registered for.</summary>
    public OrderProxy Order { get; }

    /// <summary>The guest group the callbacks were registered for; null when the call did not carry one.</summary>
    public GuestProxy? Guest { get; }

    /// <summary>The desk of <see cref="Order"/>.</summary>
    public int DeskCode => Order.DeskCode;
}

/// <summary>The day scene shop panel.</summary>
public sealed class ShopPannelView
{
    private readonly DayScene.UI.DaySceneShopPannel _panel;

    internal ShopPannelView(DayScene.UI.DaySceneShopPannel panel) => _panel = panel;

    /// <summary>Whether the opened shelf carries any product.</summary>
    public bool HasProducts => _panel.allShelfProductList is { Count: > 0 };

    public int ProductCount => _panel.allShelfProductList?.Count ?? 0;

    /// <summary>Removes the custom spacing the panel asked for while its shelf was empty.</summary>
    public void ClearCustomSpacing() =>
        ReceivedObjectDisplayerController.TryRemoveCustomSpacing<DayScene.UI.DaySceneShopPannel>();
}
