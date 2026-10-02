using Common.CharacterUtility;
using DayScene.Input;
using GameData.Core.Collections;
using GameData.Profile;
using GameData.RunTime.NightSceneUtility;
using HarmonyLib;
using NightScene.GuestManagementUtility;
using NightScene.UI.GuestManagementUtility;
using UnityEngine;

namespace Mystia.Modding.Bridge;

internal static class InputHolds
{
    [HarmonyPatch(typeof(CharacterControllerInputGeneratorComponent), nameof(CharacterControllerInputGeneratorComponent.UpdateInputDirection))]
    private static class Move
    {
        private static void Prefix(ref Vector2 inputDirection)
        {
            if (!StockGate.Move)
                inputDirection = Vector2.zero;
        }
    }

    [HarmonyPatch(typeof(DayScenePlayerInputGenerator), nameof(DayScenePlayerInputGenerator.OnSprintPerformed))]
    private static class Sprint
    {
        private static bool Prefix() => StockGate.Allow(StockGate.Sprint);
    }

    [HarmonyPatch(typeof(DayScenePlayerInputGenerator), nameof(DayScenePlayerInputGenerator.TryInteract))]
    private static class Interact
    {
        private static bool Prefix() => StockGate.Allow(StockGate.Interact);
    }
}

internal static class LeaveHolds
{
    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.PayAndLeave))]
    private static class Paid
    {
        private static bool Prefix() => StockGate.Allow(StockGate.Leave);
    }

    [HarmonyPatch(typeof(GuestsManager), "ExBadLeave")]
    private static class ExBad
    {
        private static bool Prefix() => StockGate.Allow(StockGate.Leave);
    }

    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.RepellAndLeavePay))]
    private static class RepelledPaid
    {
        private static bool Prefix() => StockGate.Allow(StockGate.Leave);
    }

    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.RepellAndLeaveNoPay))]
    private static class RepelledUnpaid
    {
        private static bool Prefix() => StockGate.Allow(StockGate.Leave);
    }

    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.PlayerRepell))]
    private static class Player
    {
        private static bool Prefix() => StockGate.Allow(StockGate.Leave);
    }

    [HarmonyPatch(typeof(GuestsManager), "PatientDepletedLeave")]
    private static class Patience
    {
        private static bool Prefix() => StockGate.Allow(StockGate.Leave);
    }

    [HarmonyPatch(typeof(GuestsManager), "LeaveFromDesk")]
    private static class Other
    {
        private static bool Prefix() => StockGate.Allow(StockGate.Leave);
    }
}

internal static class OrderHolds
{
    [HarmonyPatch(typeof(GuestsManager), "GenerateOrderSession")]
    private static class Session
    {
        private static bool Prefix(GuestGroupController guestGroup) =>
            StockGate.Allow(StockGate.Order) || PendingOrder.IsFor(guestGroup);
    }

    [HarmonyPatch(typeof(GuestGroupController), nameof(GuestGroupController.GenerateOrder))]
    private static class Generated
    {
        private static bool Prefix(
            GuestGroupController __instance,
            ref string orderGenerationMessage,
            ref GuestsManager.OrderBase generatedOrder,
            ref bool __result)
        {
            if (PendingOrder.TryTake(__instance, out var order, out var message))
            {
                generatedOrder = order;
                orderGenerationMessage = message;
                __result = true;
                return false;
            }

            return StockGate.Allow(StockGate.Order);
        }
    }
}

internal static class ServeHolds
{
    [HarmonyPatch(typeof(WorkSceneServePannel), "OnPanelClose")]
    private static class Close
    {
        private static bool Prefix(WorkSceneServePannel __instance)
        {
            if (__instance is null || StockGate.Allow(StockGate.ServeClose))
                return true;
            Return(__instance, "willServeFood");
            Return(__instance, "willServeBeverage");
            var tray = AccessTools.Field(typeof(WorkSceneServePannel), "m_CurrentTray")?.GetValue(__instance);
            tray?.GetType().GetMethod("ClosePanel")?.Invoke(tray, null);
            var director = NightScene.NightSceneDirector.instance;
            if (director is not null && director.CanGotoNextPhase())
                director.OnInteractableExit();
            return false;
        }

        private static void Return(WorkSceneServePannel panel, string field)
        {
            if (AccessTools.Field(typeof(WorkSceneServePannel), field)?.GetValue(panel) is not Sellable sellable)
                return;
            var copy = sellable.GetType().GetMethod("Duplicate")?.Invoke(sellable, null) as Sellable ?? sellable;
            IzakayaTray.Instance.Receive(copy);
        }
    }
}
