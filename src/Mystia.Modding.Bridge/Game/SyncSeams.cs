using DayScene.UI;
using GameData.Core.Collections;
using GameData.RunTime.Common;
using GameData.Utils;
using HarmonyLib;
using Mystia.Listeners;
using Mystia.Scenes;
using NightScene.GuestManagementUtility;

namespace Mystia.Modding.Bridge;

// The W1 notification batch: session/status/mission/UI listeners plus the post-evaluation notification
// that IGuestGroupListener carries. Every seam here only observes or gates one call; none of them takes
// the leave/stock gates.

internal static class SessionListenerSeams
{
    // LoadPlayerData replaces the whole player state; DisposeGameStatusAndBackToMainMenu drops it and
    // DisposeGameStatusAndRewindDay drops it back one day. All three notify before they do the work, and
    // none of them lets a listener cancel the reset.
    [HarmonyPatch(typeof(SaveManagement), nameof(SaveManagement.LoadPlayerData))]
    private static class PlayerDataLoading
    {
        private static void Prefix() => Dispatch.Run<ISessionListener>(listener => listener.OnPlayerDataLoading());
    }

    [HarmonyPatch(typeof(SaveManagement), nameof(SaveManagement.DisposeGameStatusAndBackToMainMenu))]
    private static class GameStatusResetting
    {
        private static void Prefix() => Dispatch.Run<ISessionListener>(listener => listener.OnGameStatusResetting(false));
    }

    [HarmonyPatch(typeof(SaveManagement), nameof(SaveManagement.DisposeGameStatusAndRewindDay))]
    private static class DayRewinding
    {
        private static void Prefix() => Dispatch.Run<ISessionListener>(listener => listener.OnGameStatusResetting(true));
    }
}

internal static class StatusListenerSeams
{
    [HarmonyPatch(typeof(RunTimeAlbum), nameof(RunTimeAlbum.ChangePlayerSkin))]
    private static class PlayerSkinChanging
    {
        private static void Postfix(int skinSelectionInfo) =>
            Dispatch.Run<IStatusListener>(listener => listener.OnPlayerSkinChanged(skinSelectionInfo));
    }

    // GetMap resolves the asset an izakaya's skin offset picks; the skin is already being applied when it
    // returns, so the notification runs after it. The izakaya the call sits on carries the place id, which
    // is what GetMap was asked for; engine clones keep the same Id, so the id stays the source place. When
    // there is no instance to read (null) the id falls back to -1, the same unknown marker the services use.
    [HarmonyPatch(typeof(Izakaya), nameof(Izakaya.GetMap))]
    private static class IzakayaSkinResolving
    {
        private static void Postfix(Izakaya __instance, int skinOffset)
        {
            var izakayaId = __instance is null ? -1 : __instance.Id;
            Dispatch.Run<IStatusListener>(listener => listener.OnIzakayaSkinResolved(izakayaId, skinOffset));
        }
    }
}

internal static class MissionListenerSeams
{
    // The states themselves live in conditionFinishStates and are already recomputed when this returns,
    // so the notification carries the tracked data and lets a listener read the new states.
    [HarmonyPatch(typeof(RunTimeScheduler.TrackedMissionData), nameof(RunTimeScheduler.TrackedMissionData.UpdateFinishStates))]
    private static class FinishStatesUpdating
    {
        private static void Postfix(RunTimeScheduler.TrackedMissionData __instance) =>
            Dispatch.Run<IMissionListener>(listener => listener.OnMissionFinishStatesUpdated(__instance));
    }
}

internal static class DayUiListenerSeams
{
    // The panel is handed over as the framework's own ShopPannelView, which reads HasProducts/ProductCount
    // off the panel and routes ClearCustomSpacing back into the game's displayer.
    [HarmonyPatch(typeof(DaySceneShopPannel), nameof(DaySceneShopPannel.OnPanelOpen))]
    private static class ShopPannelOpened
    {
        private static void Postfix(DaySceneShopPannel __instance) =>
            Dispatch.Run<IDayUiListener>(listener => listener.OnShopPannelOpened(new ShopPannelView(__instance)));
    }

    [HarmonyPatch(typeof(DaySceneSustainedPannel), nameof(DaySceneSustainedPannel.OnFastForwardSubmit))]
    private static class FastForwardSubmitting
    {
        private static bool Prefix()
        {
            var cancel = false;
            foreach (var listener in Dispatch.Instances<IDayUiListener>())
                listener.OnPreFastForward(ref cancel);
            return !cancel;
        }
    }
}

internal static class WorkUiListenerSeams
{
    // NightScene.UI.UIManager shares its name with DayScene.UI.UIManager, so the type is fully qualified
    // and the day scene HUD (whose Initialize takes no argument) cannot be caught by mistake.
    [HarmonyPatch(typeof(NightScene.UI.UIManager), nameof(NightScene.UI.UIManager.Initialize))]
    private static class HudOpened
    {
        private static void Postfix() => Dispatch.Run<IWorkUiListener>(listener => listener.OnHudOpened());
    }
}

internal static class GuestPostEvaluationSeams
{
    // GuestGroupController.PostEvaluation is an empty virtual body and the call dispatches to a subclass, so
    // patching only the base would never fire. Each concrete controller gets its own Postfix and the
    // EvaluationResult it resolved is passed through.
    [HarmonyPatch(typeof(NormalGuestsController), nameof(NormalGuestsController.PostEvaluation))]
    private static class NormalPostEvaluated
    {
        private static void Postfix(GuestGroupController __instance, GuestGroupController.EvaluationResult evaluationType) =>
            Dispatch.Run<IGuestGroupListener>(listener => listener.OnGroupPostEvaluated(EntitySeams.GuestHandleOf(__instance), Mirrors.ToSdk(evaluationType)));
    }

    [HarmonyPatch(typeof(SpecialGuestsController), nameof(SpecialGuestsController.PostEvaluation))]
    private static class SpecialPostEvaluated
    {
        private static void Postfix(GuestGroupController __instance, GuestGroupController.EvaluationResult evaluationType) =>
            Dispatch.Run<IGuestGroupListener>(listener => listener.OnGroupPostEvaluated(EntitySeams.GuestHandleOf(__instance), Mirrors.ToSdk(evaluationType)));
    }
}
