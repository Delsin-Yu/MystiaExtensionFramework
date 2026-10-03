using HarmonyLib;
using NightScene.CookingUtility;
using NightScene.EventUtility;

using LockLoop = GameData.Profile.YuyukoBossData.__c__DisplayClass16_6.ObjectCompilerGeneratedNPrivateSealedIEnumerator1ObjectIEnumeratorIDisposableInObSpCoObObUnique;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The cookers the framework swallowed for the challenge, held so the buff's own cleanup can unlock them the way
/// the game unlocks the ones it swallowed itself. A challenge runs one at a time, so one list is enough; it is
/// keyed by cooker index, because the same desk must never be locked twice.
/// </summary>
internal static class ChallengeCookerSwallows
{
    private static readonly List<(EventManager Manager, int Index, Il2CppSystem.Collections.Generic.IEnumerable<int> Targets)> Armed = new();

    /// <summary>
    /// Locks <paramref name="cookerIndex"/> into <paramref name="manager"/>'s locked list, so the cooker counts
    /// as unavailable exactly like the game's own swallow makes it. False when the framework already swallowed
    /// that desk, which the caller treats as success.
    /// </summary>
    internal static bool Arm(EventManager manager, int cookerIndex)
    {
        foreach (var (_, index, _) in Armed)
        {
            if (index == cookerIndex)
                return false;
        }

        var targets = new Il2CppSystem.Collections.Generic.List<int>();
        targets.Add(cookerIndex);
        var locked = targets.Cast<Il2CppSystem.Collections.Generic.IEnumerable<int>>();
        manager.LockedCookersRaw.Add(locked);
        Armed.Add((manager, cookerIndex, locked));
        return true;
    }

    /// <summary>Unlocks every cooker the framework swallowed. Harmless to call twice.</summary>
    internal static void Release()
    {
        foreach (var (manager, _, targets) in Armed)
            manager.LockedCookersRaw.Remove(targets);
        Armed.Clear();
    }

    /// <summary>
    /// Forgets the swallows of a run that ended, so the next run cannot see them as already locked. The scene
    /// drops its locked list with the challenge anyway, which is why this does not reach the event manager.
    /// </summary>
    internal static void Forget() => Armed.Clear();
}

/// <summary>
/// The retake's cooker swallow: the coroutine that eats one of the izakaya's cookers when the boss stand's
/// patience runs out. Its target is fixed inside its selection step, which is the one moment a peer can be told
/// which cooker the boss ate.
/// </summary>
internal static class ChallengeCookerSeams
{
    /// <summary>
    /// The coroutine's step that picks the cooker (its resume position after the spell declaration).
    /// </summary>
    private const int Selecting = 1;

    /// <summary>The coroutine's step that locks the picked cooker (the position the picker moves on to).</summary>
    private const int Locking = 2;

    [HarmonyPatch(typeof(LockLoop), nameof(LockLoop.MoveNext))]
    private static class Swallowed
    {
        private static void Prefix(LockLoop __instance, out int __state) => __state = __instance.__1__state;

        private static void Postfix(LockLoop __instance, int __state)
        {
            // The target is fixed exactly when the picker hands over to the locking step. The report names the
            // cooker controller the picker resolved - the cooker layer's own view of it - rather than the index
            // list the picker happens to hold, so the index is one the cooker desks agree with even when a replay
            // built the picker's state itself.
            if (__state != Selecting || __instance.__1__state != Locking)
                return;
            if (__instance.__8__1 is null)
                return;
            if (CookSystemManager.Instance?.GetCooker(__instance.__8__1.cookerPosition) is not { } cooker)
                return;
            ChallengeTimeline.Shared.CookerSwallowed(cooker.GridIndex);
        }
    }
}
