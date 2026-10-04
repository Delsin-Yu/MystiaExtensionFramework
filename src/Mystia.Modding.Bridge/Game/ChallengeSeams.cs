using GameData.Profile;
using HarmonyLib;
using Mystia.Listeners;
using Mystia.Scenes;
using NightScene.UI.HUDUtility;
using UnityEngine;

using MirrorVector3 = Mystia.Numerics.Vector3;
using Phase1SpawnLoop = GameData.Profile.YuyukoBossData.__c__DisplayClass16_0.ObjectCompilerGeneratedNPrivateSealedIEnumerator1ObjectIEnumeratorIDisposableInObWaVoObMoInVoBoOb0;
using PhaseClock = GameData.Profile.YuyukoBossData.__c__DisplayClass16_0.ObjectCompilerGeneratedNPrivateSealedIEnumerator1ObjectIEnumeratorIDisposableInObFu1BoexSiInObObUnique;
using Phase2SpawnLoop = GameData.Profile.YuyukoBossData.__c__DisplayClass16_0.ObjectCompilerGeneratedNPrivateSealedIEnumerator1ObjectIEnumeratorIDisposableInObWaVoObMoInVoBoOb1;
using Phase3SpawnLoop = GameData.Profile.YuyukoBossData.__c__DisplayClass16_6.ObjectCompilerGeneratedNPrivateSealedIEnumerator1ObjectIEnumeratorIDisposableInObWaVoObMoInVoBoOb0;
using NegativeSpellLoop = GameData.Profile.YuyukoBossData.__c__DisplayClass16_0.ObjectCompilerGeneratedNPrivateSealedIEnumerator1ObjectIEnumeratorIDisposableInObWaVoObMoInVoBoOb2;
using RunLoop = GameData.Profile.YuyukoBossData._MainChallengeLoop_d__16;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The game side of <see cref="ChallengeTimeline"/>: the seams that hook the Yuyuko challenge's own pieces and
/// hand them to the timeline, and back.
/// <para>
/// All four pieces live in compiler generated members of <c>YuyukoBossData.MainChallengeLoop</c>: the loop's
/// own coroutine state machine, the closure it captures, and the state machines of the local functions inside
/// it (the shared phase clock, the guest spawn loop of every phase that has one, and the retake's boss stand
/// spawn loop). The interop spells those names in a way that survives obfuscation - the mangled name carries
/// which state machine it is and which closure it belongs to - but the closure numbers belong to the game build
/// the interop was generated from, the build <c>Mystia.InteropGen</c> refuses to generate for anything but (it
/// pins GameAssembly.dll by hash).
/// </para>
/// <para>
/// A target named here therefore compiles against that pinned build; a build the interop is regenerated for
/// either keeps the name or fails the build of this file, and it never turns into a seam that is silently not
/// applied. The one thing a target name cannot promise is that the game's own state machine still means the
/// same piece: the shapes each seam reads (the resume position, the countdown of the clock) are what the seams
/// document next to themselves.
/// </para>
/// </summary>
internal static class ChallengeSeams
{
    private static Il2CppSystem.Object? _holdWait;

    private static bool _holdWaitFailed;

    /// <summary>
    /// The wait a held spawn loop yields. The game's own loop waits its spawn interval between two attempts, and
    /// a held attempt waits the same way instead of spinning once per frame. It is built on the first hold and
    /// reused, because holding every attempt - a peer that spawns its own phase guests - must not allocate one
    /// per frame. A build in which the wait value cannot be built falls back to waiting a single frame, which is
    /// livelier but correct, and says so once.
    /// </summary>
    internal static Il2CppSystem.Object? SpawnHold()
    {
        if (_holdWait is not null || _holdWaitFailed)
            return _holdWait;

        try
        {
            _holdWait = new UnityEngine.WaitForSeconds(1f);
        }
        catch (Exception error)
        {
            _holdWaitFailed = true;
            GameBridgeHook.Trace("ChallengeSeams: the held spawn loop has no wait value: " + error.Message);
        }

        return _holdWait;
    }
}

/// <summary>The challenge main loop: the run itself, and the steps it takes through its phases.</summary>
internal static class ChallengeRunSeams
{
    [HarmonyPatch(typeof(YuyukoBossData), nameof(YuyukoBossData.MainChallengeLoop))]
    private static class Started
    {
        private static void Postfix(Il2CppSystem.Collections.IEnumerator __result)
        {
            // The returned enumerator is the loop's own state machine, which is exactly the instance the game
            // started, so this is where a run begins and where every later seam finds its state.
            if (__result?.TryCast<RunLoop>() is not null)
                ChallengeTimeline.Shared.StartRun();
        }
    }

    [HarmonyPatch(typeof(RunLoop), nameof(RunLoop.MoveNext))]
    private static class Stepped
    {
        private static bool Prefix(RunLoop __instance, ref bool __result, out int __state)
        {
            __state = __instance.__1__state;
            Attach(__instance);
            if (ChallengeTimeline.Shared.InterceptStep(__state))
                return true;

            // A held step must not consume the resume position: the machine waits a frame where it is instead of
            // running that step, so the phase data the step would read and write is looked at again rather than
            // being skipped. The loop stays alive (the result stays true), which is not the same as the
            // challenge ending.
            __instance.__2__current = null;
            __result = true;
            return false;
        }

        private static void Postfix(RunLoop __instance, ref bool __result, int __state, bool __runOriginal)
        {
            Attach(__instance);
            ChallengeTimeline.Shared.StepRan(__state, __runOriginal);
            if (!__runOriginal || __result)
                return;
            YuyukoBossMirror.Dropped();
            ChallengeCookerSwallows.Forget();
            ChallengeTimeline.Shared.EndRun();
        }

        /// <summary>
        /// Hands the loop's closure - the challenge's status displayer and the spot its phase guests spawn at -
        /// and the run's retake flag to the timeline. All three are assigned on the loop's own early steps, so
        /// they are read on every step and are null or default before that.
        /// <para>
        /// The same step reaches the boss mirror: the shared closure holds the boss's life and the panel, the
        /// retake closure (the run's third one, only present for the retake) holds the order flag the stand
        /// spawn loop assigns, and the challenge's own controlled group is the boss a mod is handed. Every one
        /// of them is assigned on the loop's own steps, so all of them are read here on every step.
        /// </para>
        /// </summary>
        private static void Attach(RunLoop loop)
        {
            var timeline = ChallengeTimeline.Shared;
            if (!timeline.Running)
                return;

            timeline.SetRunKind(loop._isRetake_5__2 ? ChallengeRunKind.Retake : ChallengeRunKind.Story);
            var context = loop.__8__1;
            if (context is null)
                return;

            var displayer = context.statusDisplayer;
            timeline.Attach(
                displayer is null ? nint.Zero : displayer.Pointer,
                new MirrorVector3(context.yuyukoSeatPostion));
            timeline.AttachBossMirror(YuyukoBossMirror.Reached(context, loop.__8__3));
            // The game's own lookup named the boss already; the run's closure names the very same group, so it
            // is only the fallback for a build whose lookup label moved.
            if (timeline.Boss == default && context.yuyuko is not null)
                timeline.CaptureBoss(context.yuyuko.Pointer, EntitySeams.GuestHandleOf(context.yuyuko));
        }
    }
}

/// <summary>
/// The phase clock: the <c>Timing</c> routine every phase of the challenge runs, which is the phase's countdown
/// and the point the challenge advances from.
/// </summary>
internal static class ChallengeClockSeams
{
    /// <summary>The clock's step ran: the game consumed a second or is done with the clock.</summary>
    private const int Ran = 0;

    /// <summary>A listener held the second that was about to be consumed: wait a frame and ask it again.</summary>
    private const int HeldTick = 1;

    /// <summary>A listener held the phase's end: the clock waits and the phase end is asked again.</summary>
    private const int HeldStop = 2;

    /// <summary>The clock was ended from the framework: the routine finishes and the phase end was reported.</summary>
    private const int Ended = 3;

    [HarmonyPatch(typeof(PhaseClock), nameof(PhaseClock.MoveNext))]
    private static class ClockStepped
    {
        private static bool Prefix(PhaseClock __instance, ref bool __result, out int __state)
        {
            var timeline = ChallengeTimeline.Shared;

            if (__instance.__1__state == 0)
            {
                // The routine's first step is where the game reads its phase length out of the closure, so the
                // length the framework resolved (the game's own one, or the change a mod armed through the
                // services) is written there before the body runs. The first second is consumed inside this
                // same step, which is why no tick is gated here.
                var seconds = timeline.StartClock(__instance.__4__this.thisSingleRoundDuration);
                __instance.__4__this.thisSingleRoundDuration = Mathf.Max(1, Mathf.RoundToInt(seconds));
                __state = Ran;
                return true;
            }

            switch (timeline.ClockStep(__instance._totalCountDown_5__2))
            {
                case ClockAction.Run:
                    __state = Ran;
                    return true;
                case ClockAction.End:
                    // A mod ended the clock: the routine finishes here, and the phase end is reported now,
                    // because the game's own stopping path never runs for this clock and would leave the phase
                    // open. It wins over a held phase end, which is what makes it the way out of a hold.
                    __state = Ended;
                    timeline.ClockEnded(ChallengeClockStop.Ended);
                    break;
                default:
                    // Either a held second or a held phase end. Only the latter is asked again below: while the
                    // clock is held no second is consumed, so the tick gate has nothing to decide.
                    __state = timeline.ClockHeld ? HeldStop : HeldTick;
                    break;
            }

            __result = __state != Ended;
            __instance.__2__current = null;
            return false;
        }

        private static void Postfix(PhaseClock __instance, ref bool __result, int __state)
        {
            switch (__state)
            {
                case Ended:
                    return;
                case HeldTick:
                    // The held second is asked again on the next frame; the phase end is not up for decision,
                    // because the clock never reached its own stopping condition.
                    return;
                case Ran:
                    ChallengeTimeline.Shared.ClockTicked(__instance._totalCountDown_5__2, true);
                    if (__result)
                        return;
                    break;
            }

            // The game's own condition stopped the clock (a tick cannot reach this line), or a held phase end is
            // asked again: the listeners decide whether the phase may end now.
            if (ChallengeTimeline.Shared.StopClock())
            {
                __result = false;
                return;
            }

            __instance.__2__current = null;
            __result = true;
        }
    }
}

/// <summary>The guest spawn loops of the challenge's phases: one per phase that spawns guests of its own.</summary>
internal static class ChallengeSpawnSeams
{
    /// <summary>
    /// Phase one's normal guest spawn loop, which is the phase's whole content: the guests that fill the desks
    /// are what the fund target is earned from. The loop has the very shape phase two's and the retake's third
    /// phase have - an iteration spawns unless the phase has no free seat, and then waits its interval - so it
    /// is gated the same way, with the same attempt and the same hold.
    /// </summary>
    [HarmonyPatch(typeof(Phase1SpawnLoop), nameof(Phase1SpawnLoop.MoveNext))]
    private static class Phase1Guests
    {
        private static bool Prefix(Phase1SpawnLoop __instance, ref bool __result, out ChallengeSpawnAttempt __state)
        {
            __state = ChallengeTimeline.Shared.Attempt(ChallengePhase.One);
            if (ChallengeTimeline.Shared.InterceptGuestSpawn(__state))
                return true;

            __instance.__2__current = ChallengeSeams.SpawnHold();
            __result = true;
            return false;
        }

        private static void Postfix(ChallengeSpawnAttempt __state, bool __runOriginal) =>
            ChallengeTimeline.Shared.GuestSpawned(__state, __runOriginal);
    }

    /// <summary>Phase two's special guest spawn loop.</summary>
    [HarmonyPatch(typeof(Phase2SpawnLoop), nameof(Phase2SpawnLoop.MoveNext))]
    private static class Phase2Guests
    {
        private static bool Prefix(Phase2SpawnLoop __instance, ref bool __result, out ChallengeSpawnAttempt __state)
        {
            __state = ChallengeTimeline.Shared.Attempt(ChallengePhase.Two);
            if (ChallengeTimeline.Shared.InterceptGuestSpawn(__state))
                return true;

            __instance.__2__current = ChallengeSeams.SpawnHold();
            __result = true;
            return false;
        }

        private static void Postfix(ChallengeSpawnAttempt __state, bool __runOriginal) =>
            ChallengeTimeline.Shared.GuestSpawned(__state, __runOriginal);
    }

    /// <summary>
    /// The boss stand's spawn loop, which the retake of the challenge runs. The story attempt has no such loop,
    /// so its third phase spawns nothing at all.
    /// </summary>
    [HarmonyPatch(typeof(Phase3SpawnLoop), nameof(Phase3SpawnLoop.MoveNext))]
    private static class Phase3Guests
    {
        private static bool Prefix(Phase3SpawnLoop __instance, ref bool __result, out ChallengeSpawnAttempt __state)
        {
            __state = ChallengeTimeline.Shared.Attempt(ChallengePhase.Three);
            if (ChallengeTimeline.Shared.InterceptGuestSpawn(__state))
                return true;

            __instance.__2__current = ChallengeSeams.SpawnHold();
            __result = true;
            return false;
        }

        private static void Postfix(Phase3SpawnLoop __instance, ChallengeSpawnAttempt __state, bool __runOriginal)
        {
            ChallengeTimeline.Shared.GuestSpawned(__state, __runOriginal);            // This loop is the only step that assigns the retake's order flag, so it is the step the
            // framework's verdict is written at: writing it right after the step replaces the game's own
            // decision for that interval, and the flag then holds the verdict until the loop assigns it again.
            ChallengeTimeline.Shared.ReapplyBossOrder();
        }
    }
}

/// <summary>
/// The phases themselves: the challenge's own status panel is told a phase through <c>SetContext</c>, which is
/// the only place the game states which phase it is entering.
/// </summary>
/// <summary>
/// The seam of the effect the second phase runs on its own: its timed negative spell. Replacing the routine's
/// body (rather than stopping it) is what makes a suppression possible at all — the game stops the phase's
/// routines when the phase ends, and a routine that was never started cannot be stopped.
/// </summary>
internal static class ChallengeSpellSeams
{
    [HarmonyPatch(typeof(NegativeSpellLoop), nameof(NegativeSpellLoop.MoveNext))]
    private static class NegativeSpell
    {
        private static bool Prefix(NegativeSpellLoop __instance, ref bool __result)
        {
            if (ChallengeTimeline.Shared.NegativeSpellVerdict is not false)
                return true;

            // The spell is not applied on this machine. The routine keeps the shape the game's own ending has -
            // one wait and then finish - and the listeners hear about it at that ending, which is where the
            // game's own line would have been shown.
            if (__instance.__2__current is null)
            {
                __instance.__2__current = new WaitForSeconds(1f);
                Dispatch.Run<IChallengeListener>(listener => listener.OnTimedNegativeSpellSuppressed());
            }

            __result = true;
            return false;
        }
    }
}

internal static class ChallengePhaseSeams
{
    [HarmonyPatch(typeof(IncomeControllerYuyuko), nameof(IncomeControllerYuyuko.SetContext))]
    private static class Entered
    {
        private static void Postfix(IncomeControllerYuyuko __instance, int currentValue, IncomeControllerYuyuko.Phase phase)
        {
            var timeline = ChallengeTimeline.Shared;
            var challenge = phase switch
            {
                IncomeControllerYuyuko.Phase.Phase1 => ChallengePhase.One,
                IncomeControllerYuyuko.Phase.Phase2 => ChallengePhase.Two,
                IncomeControllerYuyuko.Phase.Phase3 => ChallengePhase.Three,
                _ => ChallengePhase.None,
            };
            timeline.DisplayedPhase(__instance.Pointer, challenge);
            // The third phase's context is where the panel is told the boss's life for the first time, so the
            // mirror starts here. The other two phases' values are the fund and the positive spell count.
            if (challenge == ChallengePhase.Three)
                timeline.BossLifeReported(currentValue);
        }
    }

    [HarmonyPatch(typeof(IncomeControllerYuyuko), nameof(IncomeControllerYuyuko.SetTargetProgress))]
    private static class Progressed
    {
        private static void Postfix(IncomeControllerYuyuko __instance, int targetValue)
        {
            // This panel is the shared income display of every night, so the report is kept to the run's own
            // panel and to the phase whose value is the boss's life: the same method carries the fund in the
            // first phase and the spell count in the second.
            var timeline = ChallengeTimeline.Shared;
            if (timeline.Phase != ChallengePhase.Three || !timeline.IsStatusPanel(__instance.Pointer))
                return;
            timeline.BossLifeReported(targetValue);
        }
    }
}
