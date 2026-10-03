using Common.CharacterUtility;
using Common.TimelineExtestion;
using Common.UI;
using DayScene.Input;
using GameData.Core.Collections;
using GameData.Core.Collections.CharacterUtility;
using GameData.Core.Collections.DaySceneUtility;
using GameData.Core.Collections.NightSceneUtility;
using GameData.CoreLanguage.Collections;
using GameData.Profile;
using GameData.RunTime.Common;
using GameData.RunTime.NightSceneUtility;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mystia.Listeners;
using Mystia.Scenes;
using NightScene.CookingUtility;
using NightScene.GuestManagementUtility;
using NightScene.UI.CookingUtility;
using NightScene.UI.GuestManagementUtility;
using PrepNightScene.UI;
using UnityEngine;

namespace Mystia.Modding.Bridge;

internal static class SceneSeams
{
    [HarmonyPatch(typeof(SplashScene.SceneManager), nameof(SplashScene.SceneManager.Awake))]
    private static class SplashAwake
    {
        private static void Postfix() => Dispatch.Run<ISceneListener>(listener => listener.OnSceneAwake(SceneId.Splash));
    }

    [HarmonyPatch(typeof(SplashScene.SceneManager), nameof(SplashScene.SceneManager.Start))]
    private static class SplashStart
    {
        private static void Postfix()
        {
            Dispatch.Run<ISceneListener>(listener => listener.OnSceneStart(SceneId.Splash));
            SceneLoopHost.Enter(SceneId.Splash);
        }
    }

    [HarmonyPatch(typeof(MainScene.SceneManager), nameof(MainScene.SceneManager.Awake))]
    private static class MainAwake
    {
        private static void Postfix()
        {
            Dispatch.Run<ISceneListener>(listener => listener.OnSceneAwake(SceneId.Main));
            SceneLoopHost.Enter(SceneId.Main);
        }
    }

    [HarmonyPatch(typeof(DayScene.SceneManager), nameof(DayScene.SceneManager.Awake))]
    private static class DayAwake
    {
        private static void Postfix()
        {
            Dispatch.Run<ISceneListener>(listener => listener.OnSceneAwake(SceneId.Day));
            SceneLoopHost.Enter(SceneId.Day);
        }
    }

    [HarmonyPatch(typeof(DayScene.SceneManager), nameof(DayScene.SceneManager.OnDayOver))]
    private static class DayOver
    {
        private static bool Prefix() => StockGate.Allow(StockGate.DayEnd);

        private static void Postfix() => Dispatch.Run<IDayListener>(listener => listener.OnDayEnded());
    }

    [HarmonyPatch(typeof(PrepNightScene.SceneManager), nameof(PrepNightScene.SceneManager.Start))]
    private static class PrepStart
    {
        private static void Postfix()
        {
            Dispatch.Run<ISceneListener>(listener => listener.OnSceneStart(SceneId.PrepNight));
            SceneLoopHost.Enter(SceneId.PrepNight);
        }
    }

    [HarmonyPatch(typeof(NightScene.SceneManager), nameof(NightScene.SceneManager.Start))]
    private static class NightStart
    {
        private static void Postfix()
        {
            Dispatch.Run<ISceneListener>(listener => listener.OnSceneStart(SceneId.Night));
            SceneLoopHost.Enter(SceneId.Night);
        }
    }

    [HarmonyPatch(typeof(StaffScene.SceneManager), nameof(StaffScene.SceneManager.Start))]
    private static class StaffStart
    {
        private static void Postfix()
        {
            Dispatch.Run<ISceneListener>(listener => listener.OnSceneStart(SceneId.Staff));
            SceneLoopHost.Enter(SceneId.Staff);
        }
    }

    [HarmonyPatch(typeof(ResultScene.SceneManager), nameof(ResultScene.SceneManager.Start))]
    private static class ResultStart
    {
        private static void Postfix()
        {
            Dispatch.Run<ISceneListener>(listener => listener.OnSceneStart(SceneId.Result));
            SceneLoopHost.Enter(SceneId.Result);
        }
    }

    [HarmonyPatch(typeof(RunTimeScheduler), nameof(RunTimeScheduler.OnEnterDaySceneMap))]
    private static class EnterMap
    {
        private static void Postfix() => Dispatch.Run<IDayListener>(listener => listener.OnDayMapEntered());
    }

    [HarmonyPatch(typeof(UniversalGameManager), nameof(UniversalGameManager.OpenDialogMenu))]
    private static class Dialog
    {
        private static bool Prefix(
            DialogPackage dialogPackage,
            Action onFinishCallback,
            ref Il2CppSystem.Action<Il2CppSystem.Collections.Generic.Dictionary<int, string>> overrideReplaceTextCallback)
        {
            DialogScripts.Fill(dialogPackage, ref overrideReplaceTextCallback);
            if (StockGate.Allow(StockGate.TransitionDialog) || dialogPackage?.name != "OnTransitionToNight")
                return true;
            UniversalGameManager.OpenDialogMenu(null, onFinishCallback);
            return false;
        }

        private static void Postfix(DialogPackage dialogPackage) =>
            Dispatch.Run<IDayListener>(listener => listener.OnDialogOpened(dialogPackage));
    }

    [HarmonyPatch(typeof(UniversalGameManager), nameof(UniversalGameManager.OpenDialogMenuWithExitCode))]
    private static class DialogExit
    {
        private static void Prefix(
            DialogPackage dialogPackage,
            ref Il2CppSystem.Action<Il2CppSystem.Collections.Generic.Dictionary<int, string>> overrideReplaceTextCallback) =>
            DialogScripts.Fill(dialogPackage, ref overrideReplaceTextCallback);
    }

    [HarmonyPatch(typeof(UniversalGameManager), nameof(UniversalGameManager.LoadScene))]
    private static class Load
    {
        private static void Postfix(Scene scene) => Dispatch.Run<IDayListener>(listener => listener.OnSceneChanging(scene));
    }
}

internal static class DatabaseSeams
{
    [HarmonyPatch(typeof(DataBaseCore), nameof(DataBaseCore.Initialize))]
    private static class Core
    {
        private static void Postfix() => DatabaseInject.ApplyCore();
    }

    [HarmonyPatch(typeof(DataBaseLanguage), nameof(DataBaseLanguage.Initialize))]
    private static class Language
    {
        private static void Postfix() => DatabaseInject.ApplyLanguage();
    }

    [HarmonyPatch(typeof(DataBaseCharacter), nameof(DataBaseCharacter.Initialize))]
    private static class Characters
    {
        private static void Postfix() => DatabaseInject.ApplyCharacters();
    }

    [HarmonyPatch(typeof(DataBaseDay), nameof(DataBaseDay.Initialize))]
    private static class Day
    {
        private static void Postfix() => DatabaseInject.ApplyDay();
    }

    [HarmonyPatch(typeof(NightSceneLanguage), nameof(NightSceneLanguage.Initialize))]
    private static class NightLanguage
    {
        private static void Postfix() => DatabaseInject.ApplyNightLanguage();
    }
}

internal static class CookSeams
{
    [HarmonyPatch(typeof(CookController), nameof(CookController.SetCook))]
    private static class Started
    {
        private static void Postfix(CookController __instance, Sellable thisResult, Recipe recipe, bool thisCouldReturnIngredients) =>
            Dispatch.Run<ICookListener>(listener => listener.OnCookStarted(__instance, thisResult, recipe, thisCouldReturnIngredients));
    }

    [HarmonyPatch(typeof(CookController), nameof(CookController.Extract))]
    private static class Extracted
    {
        private static void Postfix(CookController __instance) =>
            Dispatch.Run<ICookListener>(listener => listener.OnCookExtracted(__instance));
    }

    [HarmonyPatch(typeof(CookController), nameof(CookController.Store))]
    private static class Stored
    {
        private static void Postfix(CookController __instance, Sellable value) =>
            Dispatch.Run<ICookListener>(listener => listener.OnCookStored(__instance, value));
    }

    [HarmonyPatch(typeof(CookController), nameof(CookController.StartCookCountDown))]
    private static class Countdown
    {
        private static void Postfix(CookController __instance, float qteScore) =>
            Dispatch.Run<ICookListener>(listener => listener.OnCookCountdownStarted(__instance, qteScore));
    }
}

internal static class PrepSeams
{
    [HarmonyPatch(typeof(IzakayaSelectorPanel_New), "_OnGuideMapInitialize_b__21_0")]
    private static class MapConfirm
    {
        private static bool Prefix() => StockGate.Allow(StockGate.MapConfirm);

        private static void Postfix(IzakayaSelectorPanel_New __instance)
        {
            PrepPanels.Map = __instance;
            var view = PanelViewCache.GuideMap(__instance);
            Dispatch.Run<IPrepListener>(listener => listener.OnGuideMapConfirmed(view));
        }
    }

    [HarmonyPatch(typeof(IzakayaSelectorPanel_New), nameof(IzakayaSelectorPanel_New.OnGuideMapSpotSelected))]
    private static class Spot
    {
        private static void Postfix(IzakayaSelectorPanel_New __instance)
        {
            PrepPanels.Map = __instance;
            var view = PanelViewCache.GuideMap(__instance);
            Dispatch.Run<IPrepListener>(listener => listener.OnGuideSpotSelected(view));
        }
    }

    [HarmonyPatch(typeof(IzakayaConfigPannel), "_SolveDailyCompletion_b__61_7")]
    private static class Ready
    {
        private static void Postfix(IzakayaConfigPannel __instance)
        {
            PrepPanels.Config = __instance;
            var view = PanelViewCache.PrepConfig(__instance);
            Dispatch.Run<IPrepListener>(listener => listener.OnPrepConfirmed(view));
        }
    }

    [HarmonyPatch(typeof(IzakayaConfigure), nameof(IzakayaConfigure.RegisterToDailyRecipes))]
    private static class Recipe
    {
        private static void Postfix(int id) => Dispatch.Run<IPrepListener>(listener => listener.OnRecipeAdded(id));
    }

    [HarmonyPatch(typeof(IzakayaConfigure), nameof(IzakayaConfigure.RegisterToDailyBeverages))]
    private static class Beverage
    {
        private static void Postfix(int id) => Dispatch.Run<IPrepListener>(listener => listener.OnBeverageAdded(id));
    }

    [HarmonyPatch(typeof(IzakayaConfigure), nameof(IzakayaConfigure.RegisterToCookers))]
    private static class Cooker
    {
        private static void Postfix(int id, int index) =>
            Dispatch.Run<IPrepListener>(listener => listener.OnCookerAssigned(id, index));
    }

    [HarmonyPatch(typeof(IzakayaConfigure), nameof(IzakayaConfigure.LogoffFromDailyRecipes))]
    private static class LogoffRecipe
    {
        private static void Postfix(int id) => Dispatch.Run<IPrepListener>(listener => listener.OnRecipeRemoved(id));
    }

    [HarmonyPatch(typeof(IzakayaConfigure), nameof(IzakayaConfigure.LogoffFromDailyBeverages))]
    private static class LogoffBeverage
    {
        private static void Postfix(int id) => Dispatch.Run<IPrepListener>(listener => listener.OnBeverageRemoved(id));
    }

    [HarmonyPatch(typeof(IzakayaConfigure), nameof(IzakayaConfigure.LogOffFromCookers))]
    private static class LogoffCooker
    {
        private static void Postfix(int index) => Dispatch.Run<IPrepListener>(listener => listener.OnCookerRemoved(index));
    }

    [HarmonyPatch(typeof(IzakayaConfigure), nameof(IzakayaConfigure.StoreFood))]
    private static class StoreFood
    {
        private static void Postfix(Sellable sellable) =>
            Dispatch.Run<IPrepListener>(listener => listener.OnFoodStored(sellable));
    }
}

internal static class PrepPanelCache
{
    [HarmonyPatch(typeof(IzakayaSelectorPanel_New), "OnGuideMapInitialize")]
    private static class MapOpen
    {
        private static void Postfix(IzakayaSelectorPanel_New __instance)
        {
            PrepPanels.Map = __instance;
            PanelViewCache.DropGuideMap(__instance);
        }
    }

    [HarmonyPatch(typeof(IzakayaSelectorPanel_New), nameof(IzakayaSelectorPanel_New.OnGuideMapClose))]
    private static class MapClose
    {
        private static void Postfix(IzakayaSelectorPanel_New __instance) => PanelViewCache.DropGuideMap(__instance);
    }

    [HarmonyPatch(typeof(IzakayaConfigPannel), "OnPanelOpen")]
    private static class ConfigOpen
    {
        private static void Postfix(IzakayaConfigPannel __instance)
        {
            PrepPanels.Config = __instance;
            PanelViewCache.DropPrepConfig(__instance);
        }
    }

    [HarmonyPatch(typeof(IzakayaConfigPannel), nameof(IzakayaConfigPannel.OnPanelClose))]
    private static class ConfigClose
    {
        private static void Postfix(IzakayaConfigPannel __instance) => PanelViewCache.DropPrepConfig(__instance);
    }
}

internal static class WorkSeams
{
    [HarmonyPatch(typeof(WorkSceneServePannel), "OnPanelClose")]
    private static class ServeFinished
    {
        private static void Postfix(WorkSceneServePannel __instance)
        {
            var view = PanelViewCache.Serve(__instance);
            Dispatch.Run<IWorkListener>(listener => listener.OnServeFinished(view));
            PanelViewCache.DropServe(__instance);
        }
    }

    [HarmonyPatch(typeof(WorkSceneServePannel), "InvokeOrderUpdate")]
    private static class Orders
    {
        private static void Postfix(WorkSceneServePannel __instance)
        {
            var view = PanelViewCache.Serve(__instance);
            Dispatch.Run<IWorkListener>(listener => listener.OnOrdersRefreshed(view));
        }
    }

    [HarmonyPatch(typeof(WorkSceneServePannel), "Send")]
    private static class Dish
    {
        private static void Postfix(WorkSceneServePannel __instance, Sellable toSend)
        {
            var view = PanelViewCache.Serve(__instance);
            Dispatch.Run<IWorkListener>(listener => listener.OnDishSent(view, toSend));
        }
    }

    [HarmonyPatch(typeof(WorkSceneStoragePannel), "Extract")]
    private static class Storage
    {
        private static void Postfix(Sellable toExtract) =>
            Dispatch.Run<IWorkListener>(listener => listener.OnStorageExtracted(toExtract));
    }

    [HarmonyPatch(typeof(GameTimeManager), nameof(GameTimeManager.SetGameTimeMode))]
    private static class TimeMode
    {
        private static void Postfix(GameTimeManager __instance) =>
            Dispatch.Run<IWorkListener>(listener => listener.OnTimeModeChanged(__instance));
    }

    [HarmonyPatch(typeof(GameTimeManager), "OnPlayableDirectorPlayed")]
    private static class Director
    {
        private static void Postfix(GameTimeManager __instance) =>
            Dispatch.Run<IWorkListener>(listener => listener.OnTimelineDirectorPlayed(__instance));
    }
}

internal static class DayInputSeams
{
    [HarmonyPatch(typeof(CharacterControllerUnit), nameof(CharacterControllerUnit.Initialize))]
    private static class Ready
    {
        private static void Postfix(CharacterControllerUnit __instance) =>
            Dispatch.Run<IDayInputListener>(listener => listener.OnCharacterReady(__instance));
    }

    [HarmonyPatch(typeof(DayScenePlayerInputGenerator), nameof(DayScenePlayerInputGenerator.OnSprintPerformed))]
    private static class Sprint
    {
        private static void Postfix() => Dispatch.Run<IDayInputListener>(listener => listener.OnSprintStarted());
    }

    [HarmonyPatch(typeof(DayScenePlayerInputGenerator), nameof(DayScenePlayerInputGenerator.OnSprintCanceled))]
    private static class SprintStop
    {
        private static void Postfix() => Dispatch.Run<IDayInputListener>(listener => listener.OnSprintStopped());
    }

    [HarmonyPatch(typeof(DayScenePlayerInputGenerator), nameof(DayScenePlayerInputGenerator.TryInteract))]
    private static class Interact
    {
        private static void Postfix() => Dispatch.Run<IDayInputListener>(listener => listener.OnInteracted());
    }
}

internal static class GuestSeams
{
    [HarmonyPatch(typeof(GuestsManager), "PostInitializeGuestGroup")]
    private static class Spawned
    {
        private static bool Prefix(GuestGroupController initializedController)
        {
            var driver = GuestPipeline.NotifySpawned(initializedController, SpawnRequestHold.Take());
            if (driver is null)
                return true;
            driver.Start(new GuestControls(initializedController));
            return false;
        }
    }

    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.TrySendToSeat))]
    private static class Seated
    {
        private static void Postfix(GuestGroupController toTry, bool __result)
        {
            if (__result)
                Dispatch.Run<IGuestGroupListener>(listener => listener.OnGroupSeated(EntitySeams.GuestHandleOf(toTry), toTry.DeskCode));
        }
    }

    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.SetManualControlledToSeat))]
    private static class ManualSeat
    {
        private static void Postfix(GuestGroupController manualControlled, int deskCode) =>
            Dispatch.Run<IGuestGroupListener>(listener => listener.OnGroupSeated(EntitySeams.GuestHandleOf(manualControlled), deskCode));
    }

    [HarmonyPatch(typeof(GuestGroupController), nameof(GuestGroupController.GenerateOrder))]
    private static class Ordered
    {
        private static void Postfix(GuestGroupController __instance, ref GuestsManager.OrderBase generatedOrder, ref string orderGenerationMessage)
        {
            if (generatedOrder is null)
                return;
            // The order the listeners are asked about travels by handle; the handle they leave behind is what
            // the game takes, so a listener that replaces it (a replayed order) replaces it here.
            OrderHandle? order = OrderDirectory.Track(generatedOrder);
            GuestPipeline.RunOrder(__instance, ref order, ref orderGenerationMessage);
            if (EntitySeams.OrderOf(order) is { } replacement && !ReferenceEquals(replacement, generatedOrder))
                generatedOrder = replacement;
        }
    }

    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.SetNormalManualControlledOrder))]
    private static class ManualFoodOrder
    {
        private static void Postfix(GuestGroupController manualControlled) => ReplaceTop(manualControlled);
    }

    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.SetSpecialManualControlledOrder))]
    private static class ManualTagOrder
    {
        private static void Postfix(GuestGroupController manualControlled) => ReplaceTop(manualControlled);
    }

    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.EvaluateOrder))]
    private static class Evaluated
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            GuestPipeline.InsertEvaluationOverride(instructions);
    }

    [HarmonyPatch(typeof(GuestsManager), "EvaulateManualOrder")]
    private static class ManualEvaluated
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            GuestPipeline.InsertEvaluationOverride(instructions);
    }

    // Every leave seam takes the leave gate itself and pairs LeaveDispatch.Enter/Exit through __state: the
    // gate decides whether the original runs, the count decides whether this seam is the outermost leave of
    // the leave in progress and therefore the one that reports it. Report and count stay together, a gated
    // seam neither enters nor exits, and the finalizer restores the count when the original throws, because
    // HarmonyX skips the postfix then.
    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.PayAndLeave))]
    private static class Paid
    {
        private static bool Prefix(out int __state)
        {
            __state = LeaveDispatch.Enter(StockGate.Allow(StockGate.Leave));
            return __state != LeaveDispatch.Skipped;
        }

        private static void Postfix(GuestGroupController toPayAndLeave, int __state)
        {
            if (LeaveDispatch.Exit(__state))
                Dispatch.Run<IGuestGroupListener>(listener => listener.OnGroupLeft(EntitySeams.GuestHandleOf(toPayAndLeave), GuestLeaveKind.Paid));
        }

        private static void Finalizer(int __state) => LeaveDispatch.Exit(__state);
    }

    [HarmonyPatch(typeof(GuestsManager), "ExBadLeave")]
    private static class ExBad
    {
        private static bool Prefix(out int __state)
        {
            __state = LeaveDispatch.Enter(StockGate.Allow(StockGate.Leave));
            return __state != LeaveDispatch.Skipped;
        }

        private static void Postfix(GuestGroupController toExBadLeave, int __state)
        {
            if (LeaveDispatch.Exit(__state))
                Dispatch.Run<IGuestGroupListener>(listener => listener.OnGroupLeft(EntitySeams.GuestHandleOf(toExBadLeave), GuestLeaveKind.ExBad));
        }

        private static void Finalizer(int __state) => LeaveDispatch.Exit(__state);
    }

    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.RepellAndLeavePay))]
    private static class RepelledPaid
    {
        private static bool Prefix(out int __state)
        {
            __state = LeaveDispatch.Enter(StockGate.Allow(StockGate.Leave));
            return __state != LeaveDispatch.Skipped;
        }

        private static void Postfix(GuestGroupController toRepell, int __state)
        {
            if (LeaveDispatch.Exit(__state))
                Dispatch.Run<IGuestGroupListener>(listener => listener.OnGroupLeft(EntitySeams.GuestHandleOf(toRepell), GuestLeaveKind.RepelledPaid));
        }

        private static void Finalizer(int __state) => LeaveDispatch.Exit(__state);
    }

    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.RepellAndLeaveNoPay))]
    private static class RepelledUnpaid
    {
        private static bool Prefix(out int __state)
        {
            __state = LeaveDispatch.Enter(StockGate.Allow(StockGate.Leave));
            return __state != LeaveDispatch.Skipped;
        }

        private static void Postfix(GuestGroupController toRepell, int __state)
        {
            if (LeaveDispatch.Exit(__state))
                Dispatch.Run<IGuestGroupListener>(listener => listener.OnGroupLeft(EntitySeams.GuestHandleOf(toRepell), GuestLeaveKind.RepelledUnpaid));
        }

        private static void Finalizer(int __state) => LeaveDispatch.Exit(__state);
    }

    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.PlayerRepell))]
    private static class Player
    {
        private static GuestGroupController? _repelled;

        private static bool Prefix(GuestsManager __instance, int deskCode, out int __state)
        {
            _repelled = __instance.GetInDeskGuest(deskCode);
            __state = LeaveDispatch.Enter(StockGate.Allow(StockGate.Leave));
            return __state != LeaveDispatch.Skipped;
        }

        private static void Postfix(int __state)
        {
            var group = _repelled;
            _repelled = null;
            var report = LeaveDispatch.Exit(__state);
            if (report && group is not null)
                Dispatch.Run<IGuestGroupListener>(listener => listener.OnGroupLeft(EntitySeams.GuestHandleOf(group), GuestLeaveKind.PlayerRepelled));
        }

        private static void Finalizer(int __state) => LeaveDispatch.Exit(__state);
    }

    [HarmonyPatch(typeof(GuestsManager), "PatientDepletedLeave")]
    private static class Patience
    {
        private static bool Prefix(out int __state)
        {
            __state = LeaveDispatch.Enter(StockGate.Allow(StockGate.Leave));
            return __state != LeaveDispatch.Skipped;
        }

        private static void Postfix(GuestGroupController toPatientDepletedLeave, int __state)
        {
            if (LeaveDispatch.Exit(__state))
                Dispatch.Run<IGuestGroupListener>(listener => listener.OnGroupLeft(EntitySeams.GuestHandleOf(toPatientDepletedLeave), GuestLeaveKind.Patience));
        }

        private static void Finalizer(int __state) => LeaveDispatch.Exit(__state);
    }

    /// <summary>
    /// The patience countdown a queued group runs on, which is the one place a group's depletion is decided
    /// (<c>GuestsManager.AddToPatientCountdown</c> arms the callback, <c>RemoveFromPatientCountdown</c> clears
    /// it). The callback is wrapped rather than replaced: the listeners hear the depletion once, and the verdict
    /// the game armed still runs — the game's own for a group the game queued itself, the inert one a replayed
    /// queue entry armed (see <c>IWorkSceneGuests.TryQueue</c>).
    /// </summary>
    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.AddToPatientCountdown))]
    private static class QueueCountdown
    {
        private static void Postfix(GuestGroupController toCountDown)
        {
            if (toCountDown.OnPatientDepeletedCallback is not { } armed)
                return;
            toCountDown.OnPatientDepeletedCallback =
                (Il2CppSystem.Action<GuestGroupController>)(Action<GuestGroupController>)(_ => Depleted(toCountDown, armed));
        }

        /// <summary>
        /// Tells the listeners, then hands over to the verdict. UpdatePatient fires the callback on every tick
        /// while the patience is zero, so it is dropped before it runs: one depletion, one notification, one
        /// verdict.
        /// </summary>
        private static void Depleted(GuestGroupController group, Il2CppSystem.Action<GuestGroupController> armed)
        {
            group.OnPatientDepeletedCallback = null;
            Dispatch.Run<IGuestGroupListener>(
                listener => listener.OnGroupQueuePatienceDepleted(EntitySeams.GuestHandleOf(group)));
            armed.Invoke(group);
        }
    }

    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.TryCloseIzakaya))]
    private static class Closing
    {
        private static void Postfix() => Dispatch.Run<IGuestGroupListener>(listener => listener.OnIzakayaClosing());
    }

    private static void ReplaceTop(GuestGroupController group)
    {
        var property = AccessTools.Property(typeof(GuestGroupController), "AllOrdersData");
        var stack = property?.GetValue(group);
        var peek = stack?.GetType().GetMethod("Peek");
        var pop = stack?.GetType().GetMethod("Pop");
        var push = stack?.GetType().GetMethod("Push");
        if (stack is null || peek is null || pop is null || push is null)
            return;
        if (peek.Invoke(stack, null) is not GuestsManager.OrderBase previous)
            return;
        OrderHandle? current = EntitySeams.OrderHandleOf(previous);
        var message = "";
        GuestPipeline.RunOrder(group, ref current, ref message);
        if (EntitySeams.OrderOf(current) is not { } replacement || ReferenceEquals(replacement, previous))
            return;
        pop.Invoke(stack, null);
        push.Invoke(stack, [replacement]);
    }
}
