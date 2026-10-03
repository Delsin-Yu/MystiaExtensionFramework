using GameData.Core.Collections.NightSceneUtility;
using HarmonyLib;
using Mystia.Listeners;
using Mystia.Scenes;
using NightScene.GuestManagementUtility;
using UnityEngine;

namespace Mystia.Modding.Bridge;

// The request of the spawn currently in flight. PostInitializeGuestGroup ends every spawn path but is handed
// neither the spawn position nor the leave type, so the spawn prefixes leave the request they built here for
// GuestSeams.Spawned, which reports it to IGuestGroupListener.OnGroupSpawned (same shape as PendingOrder).
internal static class SpawnRequestHold
{
    private static GuestSpawnRequest _request;

    internal static void Arm(GuestSpawnRequest request) => _request = request;

    internal static GuestSpawnRequest Take()
    {
        var request = _request;
        _request = default;
        return request;
    }
}

internal static class GuestSpawnRequestSeams
{
    // The parameterless SpawnNormalGuestGroup only rolls its guests and forwards to the five argument
    // overload (GuestsManager.cs:1150), so reporting it there as well would call OnPreSpawnNormalGuests
    // twice, the first time with a request whose rewrite has nowhere to go. Only the five argument overload
    // reports, for both call paths, and that is the report whose rewrite reaches the game.
    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.SpawnNormalGuestGroup), [
        typeof(Il2CppSystem.Collections.Generic.IEnumerable<NormalGuest>),
        typeof(Il2CppSystem.Nullable<Vector3>),
        typeof(GuestGroupController.LeaveType),
        typeof(int),
        typeof(bool),
    ])]
    private static class Normal
    {
        private static bool Prefix(
            ref Il2CppSystem.Nullable<Vector3> overrideSpawnPosition,
            ref GuestGroupController.LeaveType leaveType,
            ref int targetDeskCode,
            ref bool shouldFade)
        {
            var request = Request(overrideSpawnPosition, leaveType, targetDeskCode, shouldFade);
            var cancel = false;
            foreach (var modifier in Dispatch.Instances<IGuestSpawnModifier>())
                modifier.OnPreSpawnNormalGuests(ref request, ref cancel);
            if (cancel)
                return false;

            WriteBack(request, ref overrideSpawnPosition, ref leaveType, ref targetDeskCode, ref shouldFade);
            SpawnRequestHold.Arm(request);
            return true;
        }
    }

    // Same method as GuestSpawnPipeline.SpecialId, which rewrites the id for the visual pipeline: neither
    // prefix sets a priority, so which of the two sees the other's id is not fixed.
    [HarmonyPatch(typeof(GuestsManager), nameof(GuestsManager.SpawnSpecialGuestGroup))]
    private static class Special
    {
        private static bool Prefix(
            ref int id,
            ref Il2CppSystem.Nullable<Vector3> overrideSpawnPosition,
            ref GuestGroupController.LeaveType leaveType,
            ref int targetDeskCode,
            ref bool shouldFade)
        {
            var request = Request(overrideSpawnPosition, leaveType, targetDeskCode, shouldFade);
            var guestId = id;
            var cancel = false;
            foreach (var modifier in Dispatch.Instances<IGuestSpawnModifier>())
                modifier.OnPreSpawnSpecialGuest(ref request, ref guestId, ref cancel);
            if (cancel)
                return false;

            id = guestId;
            WriteBack(request, ref overrideSpawnPosition, ref leaveType, ref targetDeskCode, ref shouldFade);
            SpawnRequestHold.Arm(request);
            return true;
        }
    }

    private static GuestSpawnRequest Request(
        Il2CppSystem.Nullable<Vector3> spawnPosition,
        GuestGroupController.LeaveType leaveType,
        int deskCode,
        bool fade) =>
        new()
        {
            // The interop proxy is a reference type, so an absent position arrives as a null or as a wrapper
            // without a value.
            SpawnPosition = spawnPosition is not null && spawnPosition.HasValue ? spawnPosition.Value : null,
            LeaveType = leaveType,
            DeskCode = deskCode,
            Fade = fade,
        };

    private static void WriteBack(
        GuestSpawnRequest request,
        ref Il2CppSystem.Nullable<Vector3> spawnPosition,
        ref GuestGroupController.LeaveType leaveType,
        ref int deskCode,
        ref bool fade)
    {
        // An absent position goes back as a null reference, which is what the services' own spawns pass
        // (SpawnNormal/SpawnSpecial hand in default) and what the game reads as "pick the spawn spot".
        spawnPosition = request.SpawnPosition.HasValue
            ? new Il2CppSystem.Nullable<Vector3>(request.SpawnPosition.Value)
            : null!;
        leaveType = request.LeaveType;
        deskCode = request.DeskCode;
        fade = request.Fade;
    }
}
