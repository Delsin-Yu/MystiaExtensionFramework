using System.Reflection;
using Common.TimelineExtestion;
using Il2CppInterop.Runtime;
using Common.UI;
using Common.UI.GlobalMap;
using GameData.Core.Collections;
using GameData.Core.Collections.CharacterUtility;
using GameData.Core.Collections.NightSceneUtility;
using GameData.Profile;
using GameData.RunTime.Common;
using GameData.RunTime.NightSceneUtility;
using HarmonyLib;
using Mystia.Listeners;
using Mystia.Scenes;
using NightScene.CookingUtility;
using NightScene.GuestManagementUtility;
using PrepNightScene.UI;
using UnityEngine;

namespace Mystia.Modding.Bridge;

internal static class GameMembers
{
    internal static void Invoke(object target, string name, params object?[] args)
    {
        var method = AccessTools.Method(target.GetType(), name)
            ?? throw new MissingMethodException(target.GetType().FullName, name);
        method.Invoke(target, args);
    }

    internal static void Set(object target, string name, object? value)
    {
        var field = AccessTools.Field(target.GetType(), name)
            ?? throw new MissingFieldException(target.GetType().FullName, name);
        field.SetValue(target, value);
    }
}

internal sealed class CommonServices : ICommonServices
{
    internal static readonly CommonServices Shared = new();

    public void LoadScene(Scene scene)
    {
        ServiceScope.Require();
        UniversalGameManager.LoadScene(scene);
    }

    public ICoroutineDispatcher Coroutines => CoroutineScheduler.Shared;

    public IDialogCatalog Dialogs => DialogCatalog.Shared;

    public IGuestRecords Records => GuestRecords.Shared;

    public void OpenDialog(DialogPackage dialog, Action onFinished)
    {
        ServiceScope.Require();
        UniversalGameManager.OpenDialogMenu(dialog, onFinished);
    }

    public void OpenDialog(
        DialogPackage dialog,
        Action onFinished,
        Action<Il2CppSystem.Collections.Generic.Dictionary<int, string>>? replaceText)
    {
        ServiceScope.Require();
        if (replaceText is null)
        {
            UniversalGameManager.OpenDialogMenu(dialog, onFinished);
            return;
        }

        var callback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Il2CppSystem.Collections.Generic.Dictionary<int, string>>>(replaceText);
        UniversalGameManager.OpenDialogMenu(dialog, onFinished, callback);
    }

    public void FadeIn(Action onFinished)
    {
        ServiceScope.Require();
        UniversalGameManager.FadeIn(onFinished);
    }

    public void FadeOut(Action onFinished)
    {
        ServiceScope.Require();
        UniversalGameManager.FadeOut(onFinished);
    }

    public void SetInputEnabled(bool enabled)
    {
        ServiceScope.Require();
        UniversalGameManager.UpdatePlayerInputAvailability(enabled);
    }

    public void SetNightTransitionEnabled(bool enabled)
    {
        ServiceScope.Require();
        StockGate.TransitionDialog = enabled;
    }
}

internal sealed class SplashSceneServices : ISplashSceneServices
{
    internal static readonly SplashSceneServices Shared = new();

    public ICommonServices Common => PresentationServices.Shared;
}

internal sealed class MainSceneServices : IMainSceneServices
{
    internal static readonly MainSceneServices Shared = new();

    public IMainSceneSessionServices Session { get; } = new SessionServices();

    public ICommonServices Common => PresentationServices.Shared;

    private sealed class SessionServices : IMainSceneSessionServices
    {
        public void GotoDay()
        {
            ServiceScope.Require();
            MainScene.SceneManager.instance.GotoDayScene(
                DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(static () => { })));
        }
    }
}

internal sealed class DaySceneServices : IDaySceneServices
{
    internal static readonly DaySceneServices Shared = new();

    public IDaySceneMapServices Map { get; } = new MapServices();

    public IDaySceneScheduleServices Schedule { get; } = new ScheduleServices();

    public IDaySceneGuestServices Guests { get; } = new GuestServices();

    public IDaySceneInputServices Input { get; } = new InputServices();

    public ICommonServices Common => PresentationServices.Shared;

    private sealed class MapServices : IDaySceneMapServices
    {
        public void RefreshSpawnMarkers()
        {
            ServiceScope.Require();
            SpawnMarkerPipeline.RefreshCurrent();
        }

        public void Swap(string mapLabel, string markerName, int travelCount, Action? onFinished = null)
        {
            ServiceScope.Require();
            var manager = DayScene.SceneManager.instance;
            if (onFinished is null)
            {
                manager.SwapMap(mapLabel, markerName, travelCount);
                return;
            }

            // The game only ever passes its own callback in, so ours goes in as onSwapFinish and every
            // other argument keeps the defaults the three argument call produces.
            manager.SwapMap(
                mapLabel,
                markerName,
                travelCount,
                onSwapFinish: DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(onFinished));
        }
    }

    private sealed class ScheduleServices : IDaySceneScheduleServices
    {
        public void SetEndEnabled(bool enabled)
        {
            ServiceScope.Require();
            StockGate.DayEnd = enabled;
        }

        public void End()
        {
            ServiceScope.Require();
            StockGate.Bypass(() => GameMembers.Invoke(DayScene.SceneManager.instance, "OnDayOver"));
        }

        public void Chat(string characterLabel)
        {
            ServiceScope.Require();
            DayScene.SceneManager.instance.Chat(characterLabel, false);
        }

        public void ReplayReward(in SchedulerNode.Reward reward)
        {
            ServiceScope.Require();
            var value = reward;
            StockGate.Bypass(() => RunTimeScheduler.ProcessReward(value));
        }
    }

    private sealed class GuestServices : IDaySceneGuestServices
    {
        public void RecordInvited(int id)
        {
            ServiceScope.Require();
            StatusTracker.Instance.RecordInvitedGuest(id);
        }
    }

    private sealed class InputServices : IDaySceneInputServices
    {
        public void SetMoveEnabled(bool enabled)
        {
            ServiceScope.Require();
            StockGate.Move = enabled;
        }

        public void SetSprintEnabled(bool enabled)
        {
            ServiceScope.Require();
            StockGate.Sprint = enabled;
        }

        public void SetInteractEnabled(bool enabled)
        {
            ServiceScope.Require();
            StockGate.Interact = enabled;
        }
    }
}

internal static class PrepPanels
{
    internal static IzakayaSelectorPanel_New? Map { get; set; }

    internal static IzakayaConfigPannel? Config { get; set; }
}

internal sealed class PrepNightSceneServices : IPrepNightSceneServices
{
    internal static readonly PrepNightSceneServices Shared = new();

    public IPrepNightMapServices Map { get; } = new MapServices();

    public IPrepNightMenuServices Menu { get; } = new MenuServices();

    public IPrepNightSessionServices Session { get; } = new SessionServices();

    public ICommonServices Common => PresentationServices.Shared;

    private sealed class MapServices : IPrepNightMapServices
    {
        public void SetConfirmEnabled(bool enabled)
        {
            ServiceScope.Require();
            StockGate.MapConfirm = enabled;
        }

        public void Confirm(IGuideMapSpot spot, IzakayaLevel level)
        {
            ServiceScope.Require();
            var panel = PrepPanels.Map ?? throw new InvalidOperationException("The guide map is not open.");
            GameMembers.Set(panel, "m_CurrentSelectedSpot", spot);
            GameMembers.Set(panel, "m_CurrentSelectedIzakayaLevel", level);
            StockGate.Bypass(() => GameMembers.Invoke(panel, "_OnGuideMapInitialize_b__21_0"));
        }
    }

    private sealed class MenuServices : IPrepNightMenuServices
    {
        public void AddRecipe(int id)
        {
            ServiceScope.Require();
            IzakayaConfigure.Instance.RegisterToDailyRecipes(id, true);
        }

        public void RemoveRecipe(int id)
        {
            ServiceScope.Require();
            IzakayaConfigure.Instance.LogoffFromDailyRecipes(id);
        }

        public void AddBeverage(int id)
        {
            ServiceScope.Require();
            IzakayaConfigure.Instance.RegisterToDailyBeverages(id, true);
        }

        public void RemoveBeverage(int id)
        {
            ServiceScope.Require();
            IzakayaConfigure.Instance.LogoffFromDailyBeverages(id);
        }

        public void AssignCooker(int id, int index)
        {
            ServiceScope.Require();
            IzakayaConfigure.Instance.RegisterToCookers(id, index, true);
        }

        public void RemoveCooker(int index)
        {
            ServiceScope.Require();
            IzakayaConfigure.Instance.LogOffFromCookers(index);
        }
    }

    private sealed class SessionServices : IPrepNightSessionServices
    {
        public void SetCompleteEnabled(bool enabled)
        {
            ServiceScope.Require();
            StockGate.PrepComplete = enabled;
        }

        public void Confirm()
        {
            ServiceScope.Require();
            var panel = PrepPanels.Config ?? throw new InvalidOperationException("The prep panel is not open.");
            var method = AccessTools.Method(typeof(IzakayaConfigPannel), "_SolveDailyCompletion_b__61_7")
                ?? AccessTools.Method(typeof(IzakayaConfigPannel), "SolveDailyCompletion")
                ?? throw new MissingMethodException(typeof(IzakayaConfigPannel).FullName, "SolveDailyCompletion");
            // The callback carries the SetCompleteEnabled gate, so the service's own press bypasses it.
            StockGate.Bypass(() => method.Invoke(panel, null));
        }

        public void ToWork()
        {
            ServiceScope.Require();
            PrepNightScene.SceneManager.instance.ToWork();
        }
    }
}

internal sealed class WorkSceneServices : IWorkSceneServices
{
    internal static readonly WorkSceneServices Shared = new();

    public IWorkSceneGuests Guests { get; } = WorkSceneGuestServing.Extend(new GuestServices());

    public IWorkSceneCook Cook { get; } = new CookServices();

    public IWorkSceneStorage Storage { get; } = new StorageServices();

    public IWorkSceneTray Tray { get; } = new TrayServices();

    public IWorkSceneTime Time { get; } = WorkSceneTimeServices.Shared;

    public IWorkSceneEconomyServices Economy { get; } = WorkSceneEconomyServices.Shared;

    public IQteServices Qte { get; } = QteServices.Shared;

    public IWorkSceneBuffs Buffs { get; } = WorkSceneBuffsServices.Shared;

    public ISpellHost Spells { get; } = WorkSceneSpellHost.Shared;

    public IWorkSceneIzakaya Izakaya { get; } = new IzakayaServices();

    public ICommonServices Common => PresentationServices.Shared;

    private sealed class GuestServices : IWorkSceneGuests
    {
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

        public GuestGroupController SpawnNormal(IReadOnlyList<NormalGuest> guests, int desk = -1)
        {
            ServiceScope.Require();
            var native = new Il2CppSystem.Collections.Generic.List<NormalGuest>(guests.Count);
            foreach (var guest in guests)
                native.Add(guest);
            var enumerable = (Il2CppSystem.Collections.Generic.IEnumerable<NormalGuest>)(object)native;
            return GuestsManager.instance.SpawnNormalGuestGroup(enumerable, default, GuestGroupController.LeaveType.Move, desk, true);
        }

        public GuestGroupController SpawnSpecial(int guestId, int desk = -1)
        {
            ServiceScope.Require();
            return GuestsManager.instance.SpawnSpecialGuestGroup(
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
        }

        public bool Seat(GuestGroupController group, int desk, bool firstSpawn = true, int seat = -1)
        {
            ServiceScope.Require();
            if (seat >= 0)
                SeatChoice.Remember(group, seat);
            var seated = false;
            StockGate.Bypass(() => seated = GuestsManager.instance.TrySendToSeat(group, firstSpawn, desk, true));
            return seated;
        }

        public GuestGroupController At(int desk)
        {
            ServiceScope.Require();
            return GuestsManager.instance.GetInDeskGuest(desk);
        }

        public void Leave(GuestGroupController group, GuestLeaveKind kind)
        {
            ServiceScope.Require();
            StockGate.Bypass(() => LeaveNow(group, kind));
        }

        public void SetPatience(GuestGroupController group, int value)
        {
            ServiceScope.Require();
            group.SetPatient(value);
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

        public void BeginOrderSession(GuestGroupController group)
        {
            ServiceScope.Require();
            StockGate.Bypass(() => GameMembers.Invoke(GuestsManager.instance, "GenerateOrderSession", group, true));
        }

        public void BeginOrderSession(GuestGroupController group, GuestsManager.OrderBase order, string message)
        {
            ServiceScope.Require();
            PendingOrder.Arm(group, order, message);
            GameMembers.Invoke(GuestsManager.instance, "GenerateOrderSession", group, true);
        }

        public void BeginOrderSession(
            GuestGroupController group,
            GuestsManager.OrderGenerationResult result,
            GuestsManager.OrderBase order,
            string message)
        {
            ServiceScope.Require();
            PendingOrder.Arm(group, order, message);
            // The order hook drops the pending order as soon as the game takes it, so the result is kept
            // next to it for the whole session (see OrderHolds); the legacy overload arms no result.
            PendingOrderResult.Arm(group, result);
            GameMembers.Invoke(GuestsManager.instance, "GenerateOrderSession", group, true);
        }

        public void Evaluate(GuestGroupController group)
        {
            ServiceScope.Require();
            // EvaluateOrder carries the SetEvaluationEnabled gate; the service's own evaluation bypasses it.
            StockGate.Bypass(() => GuestsManager.instance.EvaluateOrder(group, false, null));
        }
    }

    private sealed class CookServices : IWorkSceneCook
    {
        public void SetCallEnabled(bool enabled)
        {
            ServiceScope.Require();
            StockGate.CookCall = enabled;
        }

        public void Start(int cookerIndex, Sellable result, Recipe recipe)
        {
            ServiceScope.Require();
            Cooker(cookerIndex).SetCook(result, recipe, false);
        }

        public void Extract(int cookerIndex)
        {
            ServiceScope.Require();
            Cooker(cookerIndex).Extract(null);
        }

        public void Store(int cookerIndex, Sellable value)
        {
            ServiceScope.Require();
            Cooker(cookerIndex).Store(value);
        }

        public void StartCountdown(int cookerIndex, float qteScore)
        {
            ServiceScope.Require();
            Cooker(cookerIndex).StartCookCountDown(qteScore, false);
        }

        private static CookController Cooker(int cookerIndex)
        {
            foreach (var item in (System.Collections.IEnumerable)CookSystemManager.instance.AllCookerControllers)
            {
                if (item is CookController cooker && cooker.GridIndex == cookerIndex)
                    return cooker;
            }

            throw new InvalidOperationException($"No cooker at index {cookerIndex}.");
        }
    }

    private sealed class StorageServices : IWorkSceneStorage
    {
        public void Store(Sellable sellable)
        {
            ServiceScope.Require();
            IzakayaConfigure.Instance.StoreFood(sellable);
        }
    }

    private sealed class TrayServices : IWorkSceneTray
    {
        public void SetCloseEnabled(bool enabled)
        {
            ServiceScope.Require();
            StockGate.ServeClose = enabled;
        }

        public void Receive(Sellable sellable)
        {
            ServiceScope.Require();
            IzakayaTray.Instance.Receive(sellable);
        }
    }

    private sealed class TimeServices : IWorkSceneTime
    {
        public void SetMode(GameTimeManager.TimeMode mode)
        {
            ServiceScope.Require();
            GameTimeManager.instance.SetGameTimeMode(mode);
        }
    }

    private sealed class IzakayaServices : IWorkSceneIzakaya
    {
        public void SetCloseEnabled(bool enabled)
        {
            ServiceScope.Require();
            StockGate.IzakayaClose = enabled;
        }

        public void SetTimeCloseEnabled(bool enabled)
        {
            ServiceScope.Require();
            BridgeGates.TimeClose = enabled;
        }

        public void Close()
        {
            ServiceScope.Require();
            StockGate.Bypass(() => GuestsManager.instance.TryCloseIzakaya());
        }
    }
}

internal sealed class StaffSceneServices : IStaffSceneServices
{
    internal static readonly StaffSceneServices Shared = new();

    public ICommonServices Common => PresentationServices.Shared;
}

internal sealed class ResultSceneServices : IResultSceneServices
{
    internal static readonly ResultSceneServices Shared = new();

    public ICommonServices Common => PresentationServices.Shared;
}
