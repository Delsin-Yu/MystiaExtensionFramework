using Common.TimelineExtestion;
using Common.UI;
using Common.UI.GlobalMap;
using GameData.Core.Collections;
using UnityEngine;
using GameData.Core.Collections.NightSceneUtility;
using GameData.Profile;
using NightScene.GuestManagementUtility;
using NightScene.PartnerUtility;

using Mystia;
using Mystia.Listeners;

namespace Mystia.Scenes;

public interface ICommonServices
{
    ICoroutineDispatcher Coroutines { get; }

    IDialogCatalog Dialogs { get; }

    IGuestRecords Records { get; }

    void LoadScene(Scene scene);

    void OpenDialog(DialogPackage dialog, Action onFinished);

    void OpenDialog(DialogPackage dialog, Action onFinished, Action<Il2CppSystem.Collections.Generic.Dictionary<int, string>>? replaceText);

    void FadeIn(Action onFinished);

    void FadeOut(Action onFinished);

    void SetInputEnabled(bool enabled);

    void SetNightTransitionEnabled(bool enabled);

    // Bridge implemented members; the default body throws until the bridge wiring lands, so an unwired
    // member fails loudly instead of silently doing nothing.

    /// <summary>Shakes the camera for the given duration, with the given strength and frequency.</summary>
    void ShakeCamera(float duration, float strength, float frequency) => throw new NotSupportedException();

    /// <summary>
    /// Plays the effect asset at a path declared like every other mod resource, at a world position, and
    /// returns a handle for stopping it early.
    /// </summary>
    IVfxHandle PlayVfx(string assetPath, Vector3 position) => throw new NotSupportedException();

    /// <summary>Plays the audio asset at a path declared like every other mod resource.</summary>
    void PlayAudio(string assetPath) => throw new NotSupportedException();

    /// <summary>World position of the player character.</summary>
    Vector3 PlayerPosition => throw new NotSupportedException();

    /// <summary>World position of the table of a desk.</summary>
    Vector3 TablePosition(int deskCode) => throw new NotSupportedException();

    /// <summary>Display text of a food tag id.</summary>
    string FoodTagText(int tagId) => throw new NotSupportedException();

    /// <summary>Display text of an evaluation level.</summary>
    string EvaluationText(int evaluation) => throw new NotSupportedException();
}

[AutoWire]
public interface ISplashSceneGameLoop
{
    void Setup(ISplashSceneServices services);

    void Update(ISplashSceneServices services, float delta);

    void Shutdown(ISplashSceneServices services);
}

public interface ISplashSceneServices
{
    ICommonServices Common { get; }
}

[AutoWire]
public interface IMainSceneGameLoop
{
    void Setup(IMainSceneServices services);

    void Update(IMainSceneServices services, float delta);

    void Shutdown(IMainSceneServices services);
}

public interface IMainSceneServices
{
    IMainSceneSessionServices Session { get; }

    ICommonServices Common { get; }
}

public interface IMainSceneSessionServices
{
    void GotoDay();
}

[AutoWire]
public interface IDaySceneGameLoop
{
    void Setup(IDaySceneServices services);

    void Update(IDaySceneServices services, float delta);

    void Shutdown(IDaySceneServices services);
}

public interface IDaySceneServices
{
    IDaySceneMapServices Map { get; }

    IDaySceneScheduleServices Schedule { get; }

    IDaySceneGuestServices Guests { get; }

    IDaySceneInputServices Input { get; }

    ICommonServices Common { get; }
}

public interface IDaySceneMapServices
{
    void Swap(string mapLabel, string markerName, int travelCount, Action? onFinished = null);

    /// <summary>Rebuilds the spawn markers of the current map from the day map data.</summary>
    void RefreshSpawnMarkers() => throw new NotSupportedException();
}

public interface IDaySceneScheduleServices
{
    void SetEndEnabled(bool enabled);

    void End();

    void Chat(string characterLabel);

    /// <summary>Replays one node reward through the game's own reward path.</summary>
    void ReplayReward(in SchedulerNode.Reward reward) => throw new NotSupportedException();
}

public interface IDaySceneInputServices
{
    void SetMoveEnabled(bool enabled);

    void SetSprintEnabled(bool enabled);

    void SetInteractEnabled(bool enabled);
}

public interface IDaySceneGuestServices
{
    void RecordInvited(int id);
}

[AutoWire]
public interface IPrepNightSceneGameLoop
{
    void Setup(IPrepNightSceneServices services);

    void Update(IPrepNightSceneServices services, float delta);

    void Shutdown(IPrepNightSceneServices services);
}

public interface IPrepNightSceneServices
{
    IPrepNightMapServices Map { get; }

    IPrepNightMenuServices Menu { get; }

    IPrepNightSessionServices Session { get; }

    ICommonServices Common { get; }
}

public interface IPrepNightMapServices
{
    void SetConfirmEnabled(bool enabled);

    void Confirm(IGuideMapSpot spot, IzakayaLevel level);
}

public interface IPrepNightMenuServices
{
    void AddRecipe(int id);

    void RemoveRecipe(int id);

    void AddBeverage(int id);

    void RemoveBeverage(int id);

    void AssignCooker(int id, int index);

    void RemoveCooker(int index);
}

public interface IPrepNightSessionServices
{
    void SetCompleteEnabled(bool enabled);

    void Confirm();

    void ToWork();
}

[AutoWire]
public interface IWorkSceneGameLoop
{
    void Setup(IWorkSceneServices services);

    void Update(IWorkSceneServices services, float delta);

    void Shutdown(IWorkSceneServices services);
}

public interface IWorkSceneServices
{
    IWorkSceneGuests Guests { get; }

    IWorkSceneEconomyServices Economy { get; }

    IQteServices Qte { get; }

    IWorkSceneCook Cook { get; }

    IWorkSceneStorage Storage { get; }

    IWorkSceneTray Tray { get; }

    IWorkSceneTime Time { get; }

    IWorkSceneIzakaya Izakaya { get; }

    /// <summary>Timed buffs of the running work scene.</summary>
    IWorkSceneBuffs Buffs => throw new NotSupportedException();

    /// <summary>The spell the running work scene is executing.</summary>
    ISpellHost Spells => throw new NotSupportedException();

    ICommonServices Common { get; }
}

public interface IWorkSceneGuests
{
    void SetSpawnEnabled(bool enabled);

    void SetSeatingEnabled(bool enabled);

    void SetLeaveEnabled(bool enabled);

    void SetOrderingEnabled(bool enabled);

    void SetEvaluationEnabled(bool enabled);

    GuestGroupController SpawnNormal(IReadOnlyList<NormalGuest> guests, int desk = -1);

    GuestGroupController SpawnSpecial(int guestId, int desk = -1);

    bool Seat(GuestGroupController group, int desk, bool firstSpawn = true, int seat = -1);

    GuestGroupController At(int desk);

    void Leave(GuestGroupController group, GuestLeaveKind kind);

    void SetPatience(GuestGroupController group, int value);

    void BeginOrderSession(GuestGroupController group);

    void BeginOrderSession(GuestGroupController group, GuestsManager.OrderBase order, string message);

    void BeginOrderSession(GuestGroupController group, GuestsManager.OrderGenerationResult result, GuestsManager.OrderBase order, string message);

    void Evaluate(GuestGroupController group);

    // Bridge implemented members; the default bodies throw until the bridge wiring lands, so an unwired
    // member fails loudly instead of silently doing nothing.

    /// <summary>
    /// Sends one beverage to a group through the game's own serving path (it goes in the air first, the
    /// landing spot is re-checked and the order is evaluated when it becomes full). Returns the beverage that
    /// was actually registered on the order, or null when the serve was dropped.
    /// </summary>
    Sellable? ServeBeverage(GuestGroupController group, Sellable beverage) => throw new NotSupportedException();

    /// <summary>Marks a beverage as being thrown to a group, or clears that mark with null.</summary>
    void SetBeverageInAir(GuestGroupController group, Sellable? beverage) => throw new NotSupportedException();

    /// <summary>Tells the partners that an order changed status (the same call the game's own serving makes).</summary>
    void NotifyOrderStatusUpdate(GuestsManager.OrderBase order, PartnerManager.OrderChangeContext context, int index) => throw new NotSupportedException();

    /// <summary>The guest groups currently seated at a desk.</summary>
    IReadOnlyList<GuestGroupController> InDeskGuests => throw new NotSupportedException();

    /// <summary>The order a group is currently considering; null when it has none.</summary>
    GuestsManager.OrderBase? PendingOrder(GuestGroupController group) => throw new NotSupportedException();

    /// <summary>Whether both the dish and the beverage of an order have been served.</summary>
    bool IsOrderFullfilled(GuestsManager.OrderBase order) => throw new NotSupportedException();
}

public interface IWorkSceneCook
{
    void SetCallEnabled(bool enabled);

    void Start(int cookerIndex, Sellable result, Recipe recipe);

    void Extract(int cookerIndex);

    void Store(int cookerIndex, Sellable value);

    void StartCountdown(int cookerIndex, float qteScore);
}

public interface IWorkSceneStorage
{
    void Store(Sellable sellable);
}

public interface IWorkSceneTray
{
    void SetCloseEnabled(bool enabled);

    void Receive(Sellable sellable);
}

public interface IWorkSceneTime
{
    void SetMode(GameTimeManager.TimeMode mode);

    // Bridge implemented members; the default bodies throw until the bridge wiring lands.

    /// <summary>Whole length of the night in seconds; writing it replaces the game's night length source.</summary>
    int WholeNightSeconds
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Gates guest spawning and the night countdown starting (covers the normal, challenge and creator box loops).</summary>
    void SetTimingEnabled(bool enabled) => throw new NotSupportedException();

    /// <summary>Starts one night with the arguments the game would have used.</summary>
    void BeginTiming() => throw new NotSupportedException();
}

public interface IWorkSceneIzakaya
{
    void SetCloseEnabled(bool enabled);

    void Close();

    /// <summary>
    /// Gates the time driven path that closes the izakaya when the countdown reaches zero, for when the
    /// player driven close cannot be gated alone.
    /// </summary>
    void SetTimeCloseEnabled(bool enabled) => throw new NotSupportedException();
}

[AutoWire]
public interface IStaffSceneGameLoop
{
    void Setup(IStaffSceneServices services);

    void Update(IStaffSceneServices services, float delta);

    void Shutdown(IStaffSceneServices services);
}

public interface IStaffSceneServices
{
    ICommonServices Common { get; }
}

[AutoWire]
public interface IResultSceneGameLoop
{
    void Setup(IResultSceneServices services);

    void Update(IResultSceneServices services, float delta);

    void Shutdown(IResultSceneServices services);
}

public interface IResultSceneServices
{
    ICommonServices Common { get; }
}

[AutoWire]
public interface IGuestSpawnModifier
{
    void OnPreSpawnNormalGuests(ref GuestSpawnRequest request, ref bool cancelInvocation) { }

    void OnPreSpawnSpecialGuest(ref GuestSpawnRequest request, ref int guestId, ref bool cancelInvocation) { }

    void OnNormalGuestsGenerating(ref List<NormalGuest> guests) { }

    void OnSpecialGuestGenerating(ref int guestId) { }

    void OnNormalGuestVisual(int guestId, ref int visualIndex) { }
}
