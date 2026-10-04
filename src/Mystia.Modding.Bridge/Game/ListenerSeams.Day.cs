using HarmonyLib;
using Mystia.Listeners;
using NightScene.GuestManagementUtility;
using Mystia.Scenes;
using UnityEngine;

namespace Mystia.Modding.Bridge;

internal static class DayListenerSeams
{
    [HarmonyPatch(typeof(DayScene.SceneManager), nameof(DayScene.SceneManager.OnFirstEnterDaySceneFinish))]
    private static class FirstEnterFinished
    {
        // Distinct from IDayListener.OnDayMapEntered, which fires on every map swap.
        private static void Postfix() => Dispatch.Run<IDayListener>(listener => listener.OnDayFirstEntered());
    }
}

internal static class DayInputListenerSeams
{
    [HarmonyPatch(
        typeof(Common.CharacterUtility.CharacterControllerInputGeneratorComponent),
        nameof(Common.CharacterUtility.CharacterControllerInputGeneratorComponent.UpdateInputDirection)
    )]
    private static class Move
    {
        // The component drives the character it holds, so the notification carries that character.
        // Same method also carries InputHolds.Move, which zeroes the direction when movement is off.
        private static void Postfix(
            Common.CharacterUtility.CharacterControllerInputGeneratorComponent __instance,
            Vector2 inputDirection
        ) =>
            DayInputPipeline.Move(
                DayInputPipeline.Describe(__instance.Character),
                new Mystia.Numerics.Vector2(inputDirection));
    }
}

internal static class GuestGroupListenerSeams
{
    // The order generation result lives in a local of GenerateOrderSession's closure local function:
    // GenerateOrderSession returns void and GuestGroupController.GenerateOrder only sees the order,
    // so no public member exposes it. The closure is compiler-generated: the interop renames it and
    // its local functions (__c__DisplayClass174_0, Method_Internal_<return>_<parameters>_<index>), so
    // both names below come off the interop dump (artifacts/interop) rather than the game source, and
    // they change with the build; a stale name does not resolve at runtime and lands in host.log
    // instead of dispatching.
    private const string OrderSessionType = "NightScene.GuestManagementUtility.GuestsManager+__c__DisplayClass174_0";
    private const string OrderSessionMethod = "Method_Internal_OrderGenerationResult_GuestGroupController_byref_OrderBase_0";

    [HarmonyPatch(OrderSessionType, OrderSessionMethod)]
    private static class OrderGenerated
    {
        private static void Postfix(
            GuestGroupController toGenerate,
            GuestsManager.OrderGenerationResult __result,
            ref GuestsManager.OrderBase orderData
        )
        {
            OrderHandle? order = EntitySeams.OrderHandleOf(orderData);
            foreach (var listener in Dispatch.Instances<IGuestGroupListener>())
                listener.OnGroupOrderGenerated(EntitySeams.GuestHandleOf(toGenerate), Mirrors.ToSdk(__result), ref order);
            if (EntitySeams.OrderOf(order) is { } replacement && !ReferenceEquals(replacement, orderData))
                orderData = replacement;
        }
    }

    [HarmonyPatch(typeof(GuestGroupController), nameof(GuestGroupController.RefreshCurrentFundAndOrder))]
    private static class Arrived
    {
        private static void Postfix(GuestGroupController __instance) =>
            Dispatch.Run<IGuestGroupListener>(listener => listener.OnGroupArrived(EntitySeams.GuestHandleOf(__instance)));
    }

    [HarmonyPatch(typeof(GuestGroupController), nameof(GuestGroupController.MoveToDesk))]
    private static class MovingToDesk
    {
        // Same method also carries SeatSeams.Move, which rewrites the seat and skips the original
        // when a seat choice is pending.
        private static void Prefix(GuestGroupController __instance, int deskCode) =>
            Dispatch.Run<IGuestGroupListener>(listener => listener.OnGroupMovingToDesk(EntitySeams.GuestHandleOf(__instance), deskCode));
    }

    [HarmonyPatch(typeof(GuestGroupController), nameof(GuestGroupController.MoveToQueue))]
    private static class Queued
    {
        private static void Postfix(GuestGroupController __instance) =>
            Dispatch.Run<IGuestGroupListener>(listener => listener.OnGroupQueued(EntitySeams.GuestHandleOf(__instance)));
    }
}
