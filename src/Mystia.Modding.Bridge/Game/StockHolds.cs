using Common.CharacterUtility;
using DayScene.Input;
using GameData.Core.Collections;
using GameData.Profile;
using GameData.RunTime.NightSceneUtility;
using HarmonyLib;
using Mystia.Listeners;
using NightScene.CookingUtility;
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

// Switches deliberately not added: Leave already covers every leave path (PayAndLeave, ExBadLeave,
// RepellAndLeavePay, RepellAndLeaveNoPay, PlayerRepell, PatientDepletedLeave, LeaveFromDesk), and
// Order covers GenerateOrderSession and GenerateOrder, while MainOrderCycle only forwards to
// GenerateOrderSession.
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
        // The notification runs before the gate, so listeners see the repel even while Leave is off; a
        // listener that cancels skips the original just like the gate does.
        private static bool Prefix(int deskCode)
        {
            var cancel = false;
            foreach (var listener in Dispatch.Instances<IGuestGroupListener>())
                listener.OnPrePlayerRepel(deskCode, ref cancel);
            return !cancel && StockGate.Allow(StockGate.Leave);
        }
    }

    [HarmonyPatch(typeof(GuestsManager), "PatientDepletedLeave")]
    private static class Patience
    {
        private static bool Prefix() => StockGate.Allow(StockGate.Leave);
    }

    // The last leave seam without its own notification. It pairs the same LeaveDispatch count the other
    // leave seams in GuestSeams use, so a LeaveFromDesk reached from another leave (PayAndLeave, ExBadLeave,
    // RepellAndLeavePay/NoPay, PlayerRepell, PatientDepletedLeave) stays inside that seam's count and only
    // the outermost seam reports; a LeaveFromDesk that starts a leave by itself reports GuestLeaveKind.Other.
    [HarmonyPatch(typeof(GuestsManager), "LeaveFromDesk")]
    private static class Other
    {
        private static bool Prefix(GuestGroupController toLeave, out int __state)
        {
            __state = LeaveDispatch.Enter(StockGate.Allow(StockGate.Leave));
            return __state != LeaveDispatch.Skipped;
        }

        private static void Postfix(GuestGroupController toLeave, int __state)
        {
            if (LeaveDispatch.Exit(__state))
                Dispatch.Run<IGuestGroupListener>(listener => listener.OnGroupLeft(EntitySeams.GuestHandleOf(toLeave), GuestLeaveKind.Other));
        }

        private static void Finalizer(int __state) => LeaveDispatch.Exit(__state);
    }
}

internal static class SeatHolds
{
    // Both automatic seating paths: on spawn (PostInitializeGuestGroup) and from the queue.
    // The services' own Seat(...) runs inside StockGate.Bypass.
    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.TrySendToSeat))]
    private static class Direct
    {
        private static bool Prefix() => StockGate.Allow(StockGate.Seating);
    }

    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.CheckAndSendFromQueue))]
    private static class FromQueue
    {
        private static bool Prefix() => StockGate.Allow(StockGate.Seating);
    }
}

internal static class OrderHolds
{
    // The order generation result lives in a local function of GenerateOrderSession's closure, which
    // C# cannot spell; the interop keeps compiler-generated names verbatim (see GuestGroupListenerSeams).
    private const string OrderSessionType = "NightScene.GuestManagementUtility.GuestsManager+<>c__DisplayClass174_0";

    private const string OrderInternalMethod = "<GenerateOrderSession>g__GenerateOrderInternal|1";

    private const string RemainingFundMethod = "<GenerateOrderSession>g__CheckRemainingFund|0";

    [HarmonyPatch(typeof(GuestsManager), "GenerateOrderSession")]
    private static class Session
    {
        private static bool Prefix(GuestGroupController guestGroup) =>
            StockGate.Allow(StockGate.Order) || PendingOrder.IsFor(guestGroup) || PendingOrderResult.IsFor(guestGroup);

        // A replayed session keeps its result readable until it returns, so the state is dropped here.
        private static void Postfix(GuestGroupController guestGroup) => PendingOrderResult.Clear(guestGroup);
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

    [HarmonyPatch(OrderSessionType, OrderInternalMethod)]
    private static class GeneratedResult
    {
        private static void Postfix(GuestGroupController toGenerate, ref GuestsManager.OrderGenerationResult __result)
        {
            if (PendingOrderResult.TryPeek(toGenerate, out var result))
                __result = result;
        }
    }

    // Special guests run the fund check after the order was generated, so a replayed result has to win
    // there as well; otherwise the local check would decide instead of the replayed one.
    [HarmonyPatch(OrderSessionType, RemainingFundMethod)]
    private static class RemainingFundResult
    {
        private static void Postfix(SpecialGuestsController toGenerate, ref GuestsManager.OrderGenerationResult __result)
        {
            if (PendingOrderResult.TryPeek(toGenerate, out var result))
                __result = result;
        }
    }
}

/// <summary>
/// <see cref="PendingOrder"/> carries the replayed order and message; a replay also carries the
/// generation result, which the gate side stores here, keyed by the same guest group.
/// </summary>
internal static class PendingOrderResult
{
    private static nint _group;

    private static GuestsManager.OrderGenerationResult _result;

    internal static void Arm(GuestGroupController group, GuestsManager.OrderGenerationResult result)
    {
        _group = group.Pointer;
        _result = result;
    }

    internal static bool IsFor(GuestGroupController group) => group is not null && group.Pointer == _group;

    internal static bool TryPeek(GuestGroupController group, out GuestsManager.OrderGenerationResult result)
    {
        if (!IsFor(group))
        {
            result = default;
            return false;
        }

        result = _result;
        return true;
    }

    internal static void Clear(GuestGroupController group)
    {
        if (!IsFor(group))
            return;
        _group = nint.Zero;
        _result = default;
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

internal static class IzakayaHolds
{
    // The services' own IWorkSceneIzakaya.Close() runs inside StockGate.Bypass.
    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.TryCloseIzakaya))]
    private static class Close
    {
        private static bool Prefix() => StockGate.Allow(StockGate.IzakayaClose);
    }
}

internal static class CookHolds
{
    // Only the tile interaction path; IWorkSceneCook.Start and friends drive the cookers directly.
    [HarmonyPatch(typeof(CookSystemManager), nameof(CookSystemManager.CallCooker))]
    private static class Call
    {
        private static bool Prefix() => StockGate.Allow(StockGate.CookCall);
    }
}
