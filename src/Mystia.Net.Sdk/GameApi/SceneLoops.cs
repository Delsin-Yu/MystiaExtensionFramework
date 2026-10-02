using Common.TimelineExtestion;
using Common.UI;
using Common.UI.GlobalMap;
using GameData.Core.Collections;
using UnityEngine;
using GameData.Core.Collections.NightSceneUtility;
using GameData.Profile;
using NightScene.GuestManagementUtility;

using Mystia;
using Mystia.Listeners;

namespace Mystia.Scenes;

public interface ICommonServices
{
    void LoadScene(Scene scene);

    void OpenDialog(DialogPackage dialog, Action onFinished);

    void FadeIn(Action onFinished);

    void FadeOut(Action onFinished);

    void SetInputEnabled(bool enabled);

    void SetNightTransitionEnabled(bool enabled);
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
    void Swap(string mapLabel, string markerName, int travelCount);
}

public interface IDaySceneScheduleServices
{
    void SetEndEnabled(bool enabled);

    void End();

    void Chat(string characterLabel);
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

    IWorkSceneCook Cook { get; }

    IWorkSceneStorage Storage { get; }

    IWorkSceneTray Tray { get; }

    IWorkSceneTime Time { get; }

    IWorkSceneIzakaya Izakaya { get; }

    ICommonServices Common { get; }
}

public interface IWorkSceneGuests
{
    void SetSpawnEnabled(bool enabled);

    void SetLeaveEnabled(bool enabled);

    void SetOrderingEnabled(bool enabled);

    GuestGroupController SpawnNormal(IReadOnlyList<NormalGuest> guests, int desk = -1);

    GuestGroupController SpawnSpecial(int guestId, int desk = -1);

    bool Seat(GuestGroupController group, int desk, bool firstSpawn = true, int seat = -1);

    GuestGroupController At(int desk);

    void Leave(GuestGroupController group, GuestLeaveKind kind);

    void SetPatience(GuestGroupController group, int value);

    void BeginOrderSession(GuestGroupController group);

    void BeginOrderSession(GuestGroupController group, GuestsManager.OrderBase order, string message);

    void Evaluate(GuestGroupController group);
}

public interface IWorkSceneCook
{
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
}

public interface IWorkSceneIzakaya
{
    void Close();
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
    void OnNormalGuestsGenerating(ref List<NormalGuest> guests) { }

    void OnSpecialGuestGenerating(ref int guestId) { }

    void OnNormalGuestVisual(int guestId, ref int visualIndex) { }
}
