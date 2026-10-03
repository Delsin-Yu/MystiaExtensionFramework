using Common.CharacterUtility;
using Common.TimelineExtestion;
using Common.UI;
using DayScene.Input;
using GameData.Core.Collections;
using GameData.Core.Collections.NightSceneUtility;
using GameData.Profile;
using GameData.RunTime.NightSceneUtility;
using NightScene.CookingUtility;
using NightScene.GuestManagementUtility;
using NightScene.UI.CookingUtility;
using UnityEngine;

using Mystia;
using Mystia.Scenes;

namespace Mystia.Listeners;

[AutoWire]
public interface IDayListener
{
    void OnDayMapEntered() { }

    void OnDayFirstEntered() { }

    void OnDayEnded() { }

    void OnDialogOpened(DialogPackage package) { }

    void OnSceneChanging(Scene scene) { }
}

[AutoWire]
public interface IDayInputListener
{
    void OnCharacterReady(CharacterControllerUnit unit) { }

    void OnMoveInput(CharacterControllerUnit unit, Vector2 direction) { }

    void OnSprintStarted() { }

    void OnSprintStopped() { }

    void OnInteracted() { }
}

[AutoWire]
public interface ICookListener
{
    void OnPreCookStarted(CookController controller, ref Sellable result, ref Recipe recipe, ref bool cancelInvocation) { }

    void OnPreCookCountdownStarted(CookController controller, ref float qteScore, ref bool cancelInvocation) { }

    void OnCookStarted(CookController controller, Sellable result, Recipe recipe, bool couldReturnIngredients) { }

    void OnCookExtracted(CookController controller) { }

    void OnCookStored(CookController controller, Sellable value) { }

    void OnCookCountdownStarted(CookController controller, float qteScore) { }
}

[AutoWire]
public interface IPrepListener
{
    void OnPreRecipeAdded(int id, ref bool cancelInvocation) { }

    void OnPreBeverageAdded(int id, ref bool cancelInvocation) { }

    void OnPreCookerAssigned(int id, int index, ref bool cancelInvocation) { }

    void OnGuideMapConfirmed(GuideMapView view);

    void OnGuideSpotSelected(GuideMapView view) { }

    void OnPrepConfirmed(PrepConfigView view);

    void OnConfigTabSelected(PrepConfigView view);

    void OnConfigureUpdated(IzakayaConfigure configure) { }

    void OnRecipeAdded(int id) { }

    void OnBeverageAdded(int id) { }

    void OnCookerAssigned(int id, int index) { }

    void OnRecipeRemoved(int id) { }

    void OnBeverageRemoved(int id) { }

    void OnCookerRemoved(int index) { }

    void OnFoodStored(Sellable sellable) { }
}

[AutoWire]
public interface IWorkListener
{
    void OnServePanelOpened(ServePannelView view);

    void OnPreServePanelClosed(ServePannelView view, ref bool cancelInvocation);

    void OnPreDishServed(ServePannelView view, ref Sellable dish, ref bool cancelInvocation);

    void OnPreDishCancelled(ServePannelView view, ref Sellable dish, ref bool cancelInvocation);

    void OnPreStorageExtracted(ref Sellable sellable, ref bool cancelInvocation);

    void OnPreTimeModeSet(GameTimeManager manager, ref GameTimeManager.TimeMode mode, ref bool cancelInvocation);

    void OnServeFinished(ServePannelView view) { }

    void OnOrdersRefreshed(ServePannelView view) { }

    void OnDishSent(ServePannelView view, Sellable dish) { }

    void OnStorageExtracted(Sellable sellable) { }

    void OnTimeModeChanged(GameTimeManager manager) { }

    void OnTimelineDirectorPlayed(GameTimeManager manager) { }
}

public enum GuestLeaveKind
{
    Paid,
    ExBad,
    RepelledPaid,
    RepelledUnpaid,
    PlayerRepelled,
    Patience,
    Other,
}

/// <summary>
/// The spawn parameters of the guest group being created, filled from the arguments the game is about to use.
/// The five argument <c>SpawnNormalGuestGroup</c> overload and <c>SpawnSpecialGuestGroup</c> write a rewritten
/// request back into their arguments; the parameterless <c>SpawnNormalGuestGroup</c> only forwards to the five
/// argument overload, so a rewrite made for it cannot be applied and is dropped there.
/// </summary>
public record struct GuestSpawnRequest
{
    public Vector3? SpawnPosition;

    public GuestGroupController.LeaveType LeaveType;

    public int DeskCode;

    public bool Fade;
}

[AutoWire]
public interface IGuestGroupListener
{
    void OnGroupSpawned(GuestGroupController group, GuestSpawnRequest request) { }

    void OnGroupSeated(GuestGroupController group, int desk) { }

    void OnGroupOrdered(GuestGroupController group, ref GuestsManager.OrderBase order, ref string message) { }

    void OnGroupOrderGenerated(GuestGroupController group, GuestsManager.OrderGenerationResult result, ref GuestsManager.OrderBase order) { }

    void OnGroupEvaluated(GuestGroupController group, ref GuestGroupController.EvaluationResult result) { }

    /// <summary>The evaluation of a group was resolved and applied; fired after the game finished it.</summary>
    void OnGroupPostEvaluated(GuestGroupController group, GuestGroupController.EvaluationResult result) { }

    void OnGroupArrived(GuestGroupController group) { }

    void OnGroupMovingToDesk(GuestGroupController group, int desk) { }

    void OnGroupQueued(GuestGroupController group) { }

    void OnPrePlayerRepel(int deskCode, ref bool cancelInvocation) { }

    void OnGroupLeft(GuestGroupController group, GuestLeaveKind kind) { }

    void OnIzakayaClosing() { }
}

[AutoWire]
public interface IPortraitProvider
{
    bool TryResolvePortrait(ClothesProfile.Clothes clothes, out Sprite sprite);
}

[AutoWire]
public interface IGuestDirector
{
    IGuestDriver? Claim(GuestGroupController group);
}

public interface IGuestDriver
{
    void Start(IGuestControls controls);
}

public interface IGuestControls
{
    void Seat(int desk, int mood, Action onSeated);

    void OrderFoodAndBeverage(int foodId, int beverageId, Action<GuestGroupController.EvaluationResult> onEvaluated);

    void OrderByTag(int foodTag, int beverageTag, Action<GuestGroupController.EvaluationResult> onEvaluated);

    void SetPatience(int value);

    void Leave(bool instantly);
}
