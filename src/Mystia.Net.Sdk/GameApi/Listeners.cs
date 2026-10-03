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

using NumericsVector3 = Mystia.Numerics.Vector3;

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

    /// <summary>
    /// The deferred callbacks of one serve panel opening were registered; the panel runs them after it opens
    /// (in throw deliver mode after the animation lands). This is where a listener remembers what the order of
    /// <paramref name="callbacks"/> looked like when that happened, which <c>OnPreServeCallback</c> is later
    /// asked to judge it by.
    /// </summary>
    void OnServeCallbacksRegistered(ServeCallbackView callbacks) { }

    /// <summary>
    /// One deferred callback of <paramref name="callbacks"/> is about to run. Set <c>cancelInvocation</c> to
    /// drop it, which is how the callbacks of an order that moved on are kept from evaluating the desk's next
    /// order or writing the previous dish onto it.
    /// </summary>
    void OnPreServeCallback(ServeCallbackView callbacks, ServeCallbackKind kind, ref bool cancelInvocation) { }

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
/// <remarks>
/// The position is the framework's own mirrored value type (see <c>Mystia.Numerics.Vector3</c>), so a mod that
/// rewrites a spawn never has to name a Unity type, and the leave type is the framework's
/// <see cref="GuestLeaveType"/> mirror rather than the game's enum.
/// </remarks>
public record struct GuestSpawnRequest
{
    public NumericsVector3? SpawnPosition;

    public GuestLeaveType LeaveType;

    public int DeskCode;

    public bool Fade;
}

/// <summary>
/// What happens to a guest group of the running night. Every member names the group by its
/// <see cref="GuestHandle"/> (never the game's controller), so a listener may compare handles, store them for
/// later in the same night, and ask them for the group's projection when it needs a value.
/// <para>
/// A handle is only valid for the night it was minted in: a handle a listener kept past that night answers
/// false on <see cref="GuestHandle.TryGet"/>, so a stale reference is refused instead of reaching a controller
/// the game destroyed.
/// </para>
/// <para>
/// The <c>ref</c> members hand the entity by handle: a listener that wants the game to use another order (or
/// another dish) writes the handle of that entity back, and the framework resolves it. A listener can only
/// write a handle the framework minted for it — it cannot build one.
/// </para>
/// </summary>
[AutoWire]
public interface IGuestGroupListener
{
    void OnGroupSpawned(GuestHandle group, GuestSpawnRequest request) { }

    void OnGroupSeated(GuestHandle group, int desk) { }

    void OnGroupOrdered(GuestHandle group, ref OrderHandle? order, ref string message) { }

    void OnGroupOrderGenerated(GuestHandle group, OrderGenerationOutcome result, ref OrderHandle? order) { }

    void OnGroupEvaluated(GuestHandle group, ref GuestEvaluation result) { }

    /// <summary>The evaluation of a group was resolved and applied; fired after the game finished it.</summary>
    void OnGroupPostEvaluated(GuestHandle group, GuestEvaluation result) { }

    void OnGroupArrived(GuestHandle group) { }

    void OnGroupMovingToDesk(GuestHandle group, int desk) { }

    void OnGroupQueued(GuestHandle group) { }

    void OnPrePlayerRepel(int deskCode, ref bool cancelInvocation) { }

    void OnGroupLeft(GuestHandle group, GuestLeaveKind kind) { }

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
