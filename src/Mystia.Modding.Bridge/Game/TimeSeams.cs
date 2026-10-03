using System;
using Common.TimelineExtestion;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mystia.Scenes;
using NightScene.EventUtility;

namespace Mystia.Modding.Bridge;

// Switches added by the work scene time/economy batch that StockGate does not have yet. They are meant to be
// merged into StockGate (fields plus its Reset); until then the bridge's own seams read them directly. They
// reuse StockGate.Bypass for the services' own replays instead of adding a second bypass counter, so a gate
// is open when the switch is on or a bypass is in flight.
internal static class BridgeGates
{
    // Gates EventManager.StartGuestSpawningAndTiming, i.e. whether the night starts its countdown and loops.
    internal static bool Timing = true;

    // Gates the time driven close of the izakaya (the countdown tick that reaches zero).
    internal static bool TimeClose = true;

    // Gates the popularity tag edit path.
    internal static bool PopularityTags = true;

    internal static void Reset(SceneId scene)
    {
        // The popularity tag is read wherever sellables are shown, so it is restored for every game scene;
        // the other switches belong to the work scene alone.
        PopularityTags = true;
        if (scene is not SceneId.Night)
            return;

        Timing = true;
        TimeClose = true;
    }
}

internal sealed class WorkSceneTimeServices : IWorkSceneTime
{
    internal static readonly WorkSceneTimeServices Shared = new();

    // The arguments the game passed to the held back StartGuestSpawningAndTiming, replayed by BeginTiming.
    private static int _pendingSeconds;

    internal static void RememberTiming(int gameTotalSeconds) => _pendingSeconds = gameTotalSeconds;

    public void SetMode(GameTimeManager.TimeMode mode)
    {
        ServiceScope.Require();
        GameTimeManager.instance.SetGameTimeMode(mode);
    }

    public int WholeNightSeconds
    {
        get
        {
            ServiceScope.Require();
            return EventManager.Instance.GetWholeNightTime.Invoke();
        }
        set
        {
            ServiceScope.Require();
            EventManager.Instance.GetWholeNightTime =
                DelegateSupport.ConvertDelegate<Il2CppSystem.Func<int>>(new Func<int>(() => value));
        }
    }

    public void SetTimingEnabled(bool enabled)
    {
        ServiceScope.Require();
        BridgeGates.Timing = enabled;
    }

    public void BeginTiming()
    {
        ServiceScope.Require();
        // The game always calls StartGuestSpawningAndTiming with a positive length, so the saved value is
        // used; the current night length is the fallback for a replay that never saw the game's own call.
        var seconds = _pendingSeconds > 0 ? _pendingSeconds : EventManager.Instance.GetWholeNightTime.Invoke();
        StockGate.Bypass(() => EventManager.Instance.StartGuestSpawningAndTiming(seconds));
    }
}

internal static class TimeSeams
{
    [HarmonyPatch(typeof(EventManager), nameof(EventManager.StartGuestSpawningAndTiming))]
    private static class SpawningAndTiming
    {
        // The game's own start of the night is held back so a mod can run its opening work first;
        // WorkSceneTimeServices.BeginTiming replays it with these arguments inside StockGate.Bypass.
        private static bool Prefix(int gameTotalSeconds)
        {
            WorkSceneTimeServices.RememberTiming(gameTotalSeconds);
            return StockGate.Allow(BridgeGates.Timing);
        }
    }

    [HarmonyPatch(typeof(EventManager), nameof(EventManager.ModifyTotalTime))]
    private static class TotalTime
    {
        // TimeClose gates only the tick that would drive the countdown to zero (the automatic close), not
        // every edit to the clock: positive time edits and ticks that leave time on the clock stay allowed,
        // so other consumers of ModifyTotalTime keep working while the close is held.
        private static bool Prefix(EventManager __instance, int time)
        {
            if (StockGate.Allow(BridgeGates.TimeClose) || time >= 0)
                return true;

            return __instance.TotalCountDown + __instance.extraCountDown + time > 0;
        }
    }

    // IWorkSceneGuests.SetSpawnEnabled already drives EventManager.ShouldGuestSpawn, so the spawn switch is
    // read from there: with it off the automatic instantiate loops are not even started, which is what the
    // mod used to get from its own prefix (the cheat flow rate decision is the mod's, not the bridge's).
    [HarmonyPatch(typeof(EventManager), nameof(EventManager.StartGuestInstantiateLoop))]
    private static class GuestInstantiateLoop
    {
        private static bool Prefix(EventManager __instance)
        {
            if (__instance.ShouldGuestSpawn)
                return true;

            StopCreatorBox(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(EventManager), nameof(EventManager.StartChallengeGuestInstantiateLoop))]
    private static class ChallengeInstantiateLoop
    {
        private static bool Prefix(EventManager __instance)
        {
            if (__instance.ShouldGuestSpawn)
                return true;

            StopCreatorBox(__instance);
            return false;
        }
    }

    // The creator box coroutine is the one spawn loop that ignores EventManager.ShouldGuestSpawn, so the
    // spawn switch has to stop it as well; it is started inside StartGuestInstantiateLoop, and stopping it
    // here also clears a coroutine still running from before the switch was closed.
    private static void StopCreatorBox(EventManager instance)
    {
        var loop = instance.onCreatorBoxGuestInstantiateLoop;
        if (loop is null)
            return;

        instance.StopCoroutine(loop);
        instance.onCreatorBoxGuestInstantiateLoop = null;
    }
}
