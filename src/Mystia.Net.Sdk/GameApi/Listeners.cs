using Common.CharacterUtility;
using Common.TimelineExtestion;
using Common.UI;
using DayScene.Input;
using GameData.Core.Collections;
using GameData.Core.Collections.NightSceneUtility;
using GameData.Profile;
using NightScene.CookingUtility;
using NightScene.GuestManagementUtility;
using NightScene.UI.CookingUtility;
using NightScene.UI.GuestManagementUtility;
using PrepNightScene.UI;
using UnityEngine;

using Mystia;

namespace Mystia.Listeners;

[AutoWire]
public interface IDayListener
{
    void OnDayMapEntered() { }

    void OnDayEnded() { }

    void OnDialogOpened() { }

    void OnSceneChanging(Scene scene) { }
}

[AutoWire]
public interface IDayInputListener
{
    void OnCharacterReady(CharacterControllerUnit unit) { }

    void OnMoveInput(Vector2 direction) { }

    void OnSprintStarted() { }

    void OnSprintStopped() { }

    void OnInteracted() { }
}

[AutoWire]
public interface ICookListener
{
    void OnCookStarted(CookController controller, Sellable result, Recipe recipe, bool couldReturnIngredients) { }

    void OnCookExtracted(CookController controller) { }

    void OnCookStored(CookController controller, Sellable value) { }

    void OnCookCountdownStarted(CookController controller, float qteScore) { }
}

[AutoWire]
public interface IPrepListener
{
    void OnGuideMapConfirmed(IzakayaSelectorPanel_New panel) { }

    void OnGuideSpotSelected(IzakayaSelectorPanel_New panel) { }

    void OnPrepConfirmed(IzakayaConfigPannel panel) { }

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
    void OnServeFinished(WorkSceneServePannel panel) { }

    void OnOrdersRefreshed(WorkSceneServePannel panel) { }

    void OnDishSent(WorkSceneServePannel panel, Sellable dish) { }

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

[AutoWire]
public interface IGuestGroupListener
{
    void OnGroupSpawned(GuestGroupController group) { }

    void OnGroupSeated(GuestGroupController group, int desk) { }

    void OnGroupOrdered(GuestGroupController group, ref GuestsManager.OrderBase order, ref string message) { }

    void OnGroupEvaluated(GuestGroupController group, ref GuestGroupController.EvaluationResult result) { }

    void OnGroupLeft(GuestGroupController group, GuestLeaveKind kind) { }

    void OnIzakayaClosing() { }
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
