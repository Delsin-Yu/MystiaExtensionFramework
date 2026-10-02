using System.Collections;
using HarmonyLib;
using Mystia.Listeners;
using Mystia.Scenes;
using NightScene.GuestManagementUtility;
using NightScene.Tiles;
using UnityEngine;

namespace Mystia.Modding.Bridge;

internal static class SeatChoice
{
    private static readonly Dictionary<nint, int> Seats = new();

    internal static void Remember(GuestGroupController group, int seat) => Seats[group.Pointer] = seat;

    internal static bool Take(GuestGroupController group, out int seat)
    {
        if (!Seats.Remove(group.Pointer, out seat))
        {
            seat = -1;
            return false;
        }

        return true;
    }
}

internal static class PendingOrder
{
    private static nint _group;
    private static GuestsManager.OrderBase? _order;
    private static string? _message;

    internal static void Arm(GuestGroupController group, GuestsManager.OrderBase order, string message)
    {
        _group = group.Pointer;
        _order = order;
        _message = message ?? "";
    }

    internal static bool IsFor(GuestGroupController group) => group is not null && group.Pointer == _group && _order is not null;

    internal static bool TryTake(GuestGroupController group, out GuestsManager.OrderBase order, out string message)
    {
        if (!IsFor(group) || _order is null)
        {
            order = null!;
            message = "";
            return false;
        }

        order = _order;
        message = _message ?? "";
        _group = nint.Zero;
        _order = null;
        _message = null;
        return true;
    }
}

internal static class SeatSeams
{
    [HarmonyPatch(typeof(GuestGroupController), nameof(GuestGroupController.MoveToDesk))]
    private static class Move
    {
        private static bool Prefix(GuestGroupController __instance, int deskCode, Action onMovementFinishCallback)
        {
            if (!SeatChoice.Take(__instance, out var seat))
                return true;
            WalkToSeat(__instance, deskCode, seat, onMovementFinishCallback);
            return false;
        }

        private static void WalkToSeat(GuestGroupController group, int deskCode, int seat, Action onMovementFinishCallback)
        {
            if (group.queued)
                group.RemoveFromQueue();
            var desk = TileManager.Instance.GuestTables[deskCode];
            var seats = new List<Vector3Int>();
            foreach (var item in (IEnumerable)desk.seatPositions)
            {
                if (item is Vector3Int cell)
                    seats.Add(cell);
            }

            group.DeskCode = deskCode;
            var moving = group.guestInstances.Length;
            group.OnMoveToSeatCallback?.Invoke(desk.tablePosition, group);
            for (var index = 0; index < group.guestInstances.Length; index++)
            {
                var guest = group.guestInstances[index];
                var chosen = seat < 0 || seat >= seats.Count ? UnityEngine.Random.Range(0, seats.Count) : seat;
                var cell = seats[chosen];
                seats.RemoveAt(chosen);
                seat = -1;
                var delay = index * 0.2f;
                guest.SetPath(
                    cell,
                    group.tileManager.GetCollider(cell, (Il2CppSystem.Collections.Generic.IReadOnlyList<Vector3Int>)(object)group.tileManager.PasserBorder),
                    delay,
                    (Action)(() =>
                    {
                        moving--;
                        if (moving == 0)
                            onMovementFinishCallback?.Invoke();
                    }),
                    TileManager.FindDirection(cell, desk.tablePosition));
            }
        }
    }
}
