using System.Reflection;
using HarmonyLib;
using Mystia.Listeners;
using Mystia.Modding.Bridge;
using Mystia.Numerics;
using Mystia.Scenes;
using NightScene.GuestManagementUtility;
using Xunit;

namespace Mystia.Tests;

// The challenge timeline is process wide (ChallengeTimeline.Shared) and its listener pipeline reads the
// registry BridgeInstaller hands out, so this class must not run in parallel with the other suites that bind
// one (the xunit default parallelises test classes). Everything in this file is pure managed state: no seam of
// the game side runs, which is the point of keeping the timeline free of game types.
[CollectionDefinition("Challenge timeline", DisableParallelization = true)]
public sealed class ChallengeTimelineCollection
{
}

[Collection("Challenge timeline")]
public sealed class ChallengeTests : IDisposable
{
    private readonly ModRegistry _registry = new();

    private readonly List<ChallengeClockStop> _phaseEnds = [];

    public ChallengeTests()
    {
        ChallengeTimeline.Shared.Reset();
        BridgeInstaller.Bind(_registry);
    }

    public void Dispose()
    {
        BridgeInstaller.Bind(null);
        ChallengeTimeline.Shared.Reset();
    }

    private static ChallengeTimeline Timeline => ChallengeTimeline.Shared;

    // ---- the dispatch contract ----------------------------------------------------------------------

    [Fact]
    public void EveryListenerSeesTheEventAndTheVerdictTravelsThroughThemInOrder()
    {
        StartRun();
        var order = new List<string>();
        var first = new Recorder(order, "first");
        var second = new Recorder(order, "second").CancelStep(3);
        var third = new Recorder(order, "third").CancelStep(3);
        Listen(first, second, third);

        var ran = Timeline.InterceptStep(3);

        Assert.False(ran);
        Assert.Equal(new[] { "first:pre-step", "second:pre-step", "third:pre-step" }, order);
        // The cancellation of one listener never hides the event from the listeners after it, and each of them
        // sees the verdict the previous one left behind.
        Assert.False(second.SawCancelOnStep);
        Assert.True(third.SawCancelOnStep);
    }

    [Fact]
    public void ARanStepIsReportedAndACancelledStepIsNot()
    {
        var ran = new Recorder([], "ran");
        Listen(ran);
        Timeline.StartRun();

        var allowed = Timeline.InterceptStep(9);
        Timeline.StepRan(9, 9, allowed);
        Assert.True(allowed);
        Assert.Equal(new[] { "ran:pre-step", "ran:step-ran" }, ran.Events);

        var held = new Recorder([], "held").CancelStep(7);
        Listen(held);
        ran.Events.Clear();
        var cancelled = Timeline.InterceptStep(7);
        Timeline.StepRan(7, 7, cancelled);

        Assert.False(cancelled);
        // The hold is reported to every listener, and the step itself is not reported as ran to any of them:
        // the game did not run it.
        Assert.Equal(new[] { "ran:pre-step", "held:pre-step" }, ran.Events.Concat(held.Events));

        // A held step must not consume the resume position, which is what the cancel of the listener is for.
        Assert.True(Timeline.InterceptStep(8));
    }

    [Fact]
    public void AStepTheGameDidNotRunIsNotReportedAsRan()
    {
        var recorder = new Recorder([], "only");
        Listen(recorder);
        Timeline.StartRun();

        // The listener allows the step, but the game did not run it after all - another patch held it - and the
        // timeline only reports what the game really did.
        Assert.True(Timeline.InterceptStep(2));
        Timeline.StepRan(2, 2, false);

        Assert.Equal(new[] { "only:pre-step" }, recorder.Events);
    }

    // ---- the phase clock ----------------------------------------------------------------------------

    [Fact]
    public void TheGameLengthIsUsedUnlessOneIsArmed()
    {
        StartRun();
        Timeline.DisplayedPhase(1, ChallengePhase.One);
        Assert.Equal(60f, Timeline.StartClock(60f));

        StartRun();
        Timeline.DisplayedPhase(1, ChallengePhase.One);
        Assert.True(Timeline.ArmPhaseSeconds(ChallengePhase.One, 135f));
        Assert.Equal(135f, Timeline.StartClock(60f));
        // The clock of that phase already ran: a later change cannot be applied and says so.
        Assert.False(Timeline.ArmPhaseSeconds(ChallengePhase.One, 90f));
    }

    [Fact]
    public void APhaseClockReportsItsStartAndEverySecondItConsumes()
    {
        StartRun();
        var recorder = new Recorder([], "only");
        Listen(recorder);
        Timeline.DisplayedPhase(1, ChallengePhase.One);

        Timeline.StartClock(60f);
        Timeline.ClockTicked(60f, true);
        Assert.Equal(60f, Timeline.RemainingSeconds);

        Assert.Equal(ClockAction.Run, Timeline.ClockStep(60f));
        Timeline.ClockTicked(59f, true);

        // The first step consumes its second inside itself (which is why it is not gated), the second one is
        // announced before it consumes it and reported afterwards with what the counter holds then.
        Assert.Equal(
            new[]
            {
                "only:phase-started", "only:clock-started", "only:clock-tick:60", "only:pre-clock-tick:60",
                "only:clock-tick:59",
            },
            recorder.Events);
        Assert.Equal(59f, Timeline.RemainingSeconds);
    }

    [Fact]
    public void AHeldClockConsumesNoSecond()
    {
        StartRun();
        var recorder = new Recorder([], "only").CancelClockTick(60f);
        Listen(recorder);
        Timeline.DisplayedPhase(1, ChallengePhase.One);
        Timeline.StartClock(60f);

        Assert.Equal(ClockAction.Hold, Timeline.ClockStep(60f));
        Timeline.ClockTicked(60f, false);

        Assert.Equal(new[] { "only:phase-started", "only:clock-started", "only:pre-clock-tick:60" }, recorder.Events);
        Assert.Equal(60f, Timeline.RemainingSeconds);
        // A held second is not a held phase end: the clock never reached its own stopping condition, so the
        // phase end is not up for decision and the seam does not ask for it.
        Assert.False(Timeline.ClockHeld);
    }

    [Fact]
    public void TheStopReasonSaysWhetherTheClockRanOut()
    {
        StartRun();
        Timeline.DisplayedPhase(1, ChallengePhase.One);
        Timeline.StartClock(60f);
        Timeline.ClockTicked(60f, true);

        // The phase's own condition ended the clock while time was left on it.
        Timeline.ClockStep(41f);
        Timeline.ClockTicked(41f, true);
        Assert.True(Timeline.StopClock());
        Assert.Equal(ChallengeClockStop.ObjectiveReached, _phaseEnds[0]);

        // The clock consumed its last second, and the game restored its counter to the whole phase length.
        Timeline.Reset();
        StartRun();
        Timeline.DisplayedPhase(1, ChallengePhase.One);
        Timeline.StartClock(60f);
        Timeline.ClockTicked(60f, true);
        Timeline.ClockStep(1f);
        Timeline.ClockTicked(60f, true);

        Assert.True(Timeline.StopClock());
        Assert.Equal(ChallengeClockStop.Elapsed, _phaseEnds[0]);
    }

    [Fact]
    public void AHeldPhaseEndIsAskedAgainUntilTheListenerReleasesIt()
    {
        StartRun();
        var recorder = new Recorder([], "only").HoldClock();
        Listen(recorder);
        Timeline.DisplayedPhase(1, ChallengePhase.One);
        Timeline.StartClock(60f);
        Timeline.ClockTicked(60f, true);

        Assert.False(Timeline.StopClock());
        Assert.Empty(_phaseEnds);

        recorder.Hold = false;
        Assert.True(Timeline.StopClock());
        Assert.Equal(ChallengeClockStop.ObjectiveReached, _phaseEnds[0]);
        Assert.Equal(
            new[] { "only:phase-started", "only:clock-started", "only:clock-tick:60", "only:phase-ended" },
            recorder.Events);
    }

    [Fact]
    public void AModEndsTheClockAndTheReasonSaysSo()
    {
        StartRun();
        var recorder = new Recorder([], "only");
        Listen(recorder);
        Timeline.DisplayedPhase(1, ChallengePhase.One);
        Timeline.StartClock(60f);
        Timeline.ClockTicked(60f, true);

        Assert.True(Timeline.RequestClockEnd());
        Assert.Equal(ClockAction.End, Timeline.ClockStep(40f));

        // The seam reports the end it caused; a second report is dropped, because that clock ended once.
        Timeline.ClockEnded(ChallengeClockStop.Ended);
        Timeline.ClockEnded(ChallengeClockStop.Ended);

        Assert.Equal(ChallengeClockStop.Ended, _phaseEnds[0]);
        Assert.Equal(new[] { "only:phase-started", "only:clock-started", "only:clock-tick:60", "only:phase-ended" }, recorder.Events);
        Assert.Equal(-1f, Timeline.RemainingSeconds);
        Assert.False(Timeline.RequestClockEnd());
    }

    [Fact]
    public void APhaseEndKeepsThePhaseItEndedAndDropsTheArmedSpawnVerdict()
    {
        StartRun();
        Timeline.DisplayedPhase(1, ChallengePhase.Two);
        Timeline.StartClock(60f);
        Timeline.ArmSpawnVerdict(ChallengePhase.Two, ChallengeSpawnVerdict.Hold);

        Timeline.ClockEnded(ChallengeClockStop.Elapsed);

        // The phase is kept: the loops that phase started may run for the rest of the frame.
        Assert.Equal(ChallengePhase.Two, Timeline.Phase);
        // The verdict belonged to the phase that just ended.
        Assert.True(Timeline.InterceptGuestSpawn(Timeline.Attempt(ChallengePhase.Two)));
    }

    // ---- the phase guest spawn loops ----------------------------------------------------------------

    [Fact]
    public void AHeldIterationIsNotReportedAsSpawnedAndKeepsItsNumber()
    {
        StartRun();
        var recorder = new Recorder([], "only").CancelSpawn(ChallengePhase.Two);
        Listen(recorder);

        var attempt = Timeline.Attempt(ChallengePhase.Two);
        var ran = Timeline.InterceptGuestSpawn(attempt);
        Timeline.GuestSpawned(attempt, ran);

        Assert.False(ran);
        Assert.Equal(new[] { "only:pre-spawn:1" }, recorder.Events);
        // The held iteration did not run, so the iteration that runs next keeps its number.
        Assert.Equal(1, Timeline.Attempt(ChallengePhase.Two).Index);
    }

    [Fact]
    public void AnArmedSpawnVerdictReplacesTheListenersVoteForExactlyOneIteration()
    {
        StartRun();
        var recorder = new Recorder([], "only").CancelSpawn(ChallengePhase.Three);
        Listen(recorder);
        Timeline.ArmSpawnVerdict(ChallengePhase.Three, ChallengeSpawnVerdict.Allow);

        var allowed = Timeline.Attempt(ChallengePhase.Three);
        Assert.True(Timeline.InterceptGuestSpawn(allowed));
        Timeline.GuestSpawned(allowed, true);

        // The listeners were asked for both iterations, and the verdict steered only the first one.
        var next = Timeline.Attempt(ChallengePhase.Three);
        Assert.False(Timeline.InterceptGuestSpawn(next));
        Assert.Equal(2, next.Index);
        Assert.Equal(new[] { "only:pre-spawn:1", "only:spawned:1", "only:pre-spawn:2" }, recorder.Events);
    }

    [Fact]
    public void ASpawnVerdictBelongsToOnePhaseAndArmsAsAHold()
    {
        StartRun();
        Timeline.ArmSpawnVerdict(ChallengePhase.Two, ChallengeSpawnVerdict.Hold);

        Assert.False(Timeline.InterceptGuestSpawn(Timeline.Attempt(ChallengePhase.Two)));
        // The other phase's loop was not touched by it.
        Assert.True(Timeline.InterceptGuestSpawn(Timeline.Attempt(ChallengePhase.Three)));
    }

    [Fact]
    public void TheSpawnAttemptCarriesThePhaseTheNumberAndTheSpawnSpot()
    {
        StartRun();
        var position = new Vector3(10f, -9.5f, 0f);
        Timeline.Attach(displayer: 2, position);

        var attempt = Timeline.Attempt(ChallengePhase.Two);

        Assert.Equal(ChallengePhase.Two, attempt.Phase);
        Assert.Equal(1, attempt.Index);
        Assert.Equal(position, attempt.Position);
    }

    // ---- the run ------------------------------------------------------------------------------------

    [Fact]
    public void APanelOfAnotherChallengeIsNotThisRunsStatusDisplayer()
    {
        StartRun();
        Timeline.DisplayedPhase(11, ChallengePhase.One);
        var recorder = new Recorder([], "only");
        Listen(recorder);

        Timeline.DisplayedPhase(12, ChallengePhase.Two);

        Assert.Equal(ChallengePhase.One, Timeline.Phase);
        Assert.Empty(recorder.Events);
    }

    [Fact]
    public void ARunStartingDropsWhatTheFinishedRunLeftBehind()
    {
        StartRun();
        Timeline.DisplayedPhase(1, ChallengePhase.One);
        Timeline.StartClock(60f);
        Timeline.ArmSpawnVerdict(ChallengePhase.Two, ChallengeSpawnVerdict.Hold);

        Timeline.EndRun();

        Assert.Equal(ChallengePhase.None, Timeline.Phase);
        Assert.Equal(-1f, Timeline.RemainingSeconds);
        Assert.True(Timeline.InterceptGuestSpawn(Timeline.Attempt(ChallengePhase.Two)));
        Assert.False(Timeline.RequestClockEnd());
        // A step of a run the framework never saw is left alone instead of being held.
        Timeline.StartRun();
        Timeline.EndRun();
        Assert.True(Timeline.InterceptStep(5));
    }

    // ---- the services -------------------------------------------------------------------------------

    [Fact]
    public void TheServicesThrowOutsideTheSceneLoop()
    {
        var services = ChallengeServices.Shared;

        Assert.Throws<InvalidOperationException>(() => { _ = services.Phase; });
        Assert.Throws<InvalidOperationException>(() => { _ = services.RunKind; });
        Assert.Throws<InvalidOperationException>(() => { _ = services.RemainingSeconds; });
        Assert.Throws<InvalidOperationException>(() => { _ = services.Clock; });
        Assert.Throws<InvalidOperationException>(() => { _ = services.SetPhaseSeconds(ChallengePhase.Two, 30f); });
        Assert.Throws<InvalidOperationException>(() => services.SetNextGuestSpawn(ChallengePhase.Two, ChallengeSpawnVerdict.Hold));
        Assert.Throws<InvalidOperationException>(() => services.EndPhaseClock());
    }

    [Fact]
    public void TheServicesAnswerInsideTheSceneLoop()
    {
        var services = ChallengeServices.Shared;
        ServiceScope.Enter();
        try
        {
            StartRun();
            Timeline.DisplayedPhase(1, ChallengePhase.One);
            Timeline.StartClock(45f);

            Assert.Equal(ChallengePhase.One, services.Phase);
            Assert.Equal(ChallengeRunKind.Story, services.RunKind);
            Assert.Equal(45f, services.RemainingSeconds);
            Assert.True(services.Clock != default);
            Assert.True(services.SetPhaseSeconds(ChallengePhase.Two, 30f));
            services.SetNextGuestSpawn(ChallengePhase.Two, ChallengeSpawnVerdict.Hold);
            Assert.False(services.SetPhaseSeconds(ChallengePhase.One, 30f));
            Assert.False(services.SetPhaseSeconds(ChallengePhase.Two, 0f));
            services.EndPhaseClock();

            // Asking for the end is what the clock's next step answers with, and it is answered once.
            Assert.Equal(ClockAction.End, Timeline.ClockStep(45f));
        }
        finally
        {
            ServiceScope.Exit();
        }
    }

    [Fact]
    public void TheServicesRefuseToEndAClockThatIsNotRunning()
    {
        ServiceScope.Enter();
        try
        {
            StartRun();
            Assert.Throws<InvalidOperationException>(() => ChallengeServices.Shared.EndPhaseClock());
        }
        finally
        {
            ServiceScope.Exit();
        }
    }

    // ---- the failure story and the buff end -----------------------------------------------------------

    [Fact]
    public void AFailureStoryStartIsReportedOnlyInsideARun()
    {
        var recorder = new Recorder([], "only");
        Listen(recorder);

        // No run: the timeline owns no challenge, so another challenge's failure is not reported as this one's.
        Timeline.FailureStarted();
        Assert.Empty(recorder.Events);

        StartRun();
        Timeline.FailureStarted();

        Assert.Equal(new[] { "only:failure" }, recorder.Events);
    }

    [Fact]
    public void ABuffEndIsReportedOnlyInsideARun()
    {
        var recorder = new Recorder([], "only");
        Listen(recorder);

        Timeline.BuffEnded();
        Assert.Empty(recorder.Events);

        StartRun();
        Timeline.BuffEnded();
        Timeline.EndRun();
        Timeline.BuffEnded();

        // The run's own end is followed by its exit, whose buff teardown is not this timeline's report any more.
        Assert.Equal(new[] { "only:buff-ended" }, recorder.Events);
    }

    // ---- the boss -----------------------------------------------------------------------------------

    [Fact]
    public void ABossLifeReportMirrorsTheValueAndReportsEveryChangeOnce()
    {
        var recorder = new Recorder([], "only");
        StartRun();
        Listen(recorder);

        Assert.Equal(-1, Timeline.BossLife);

        // The panel is told the same context and the same progress more than once, so a repeat is not a change.
        Timeline.BossLifeReported(50);
        Timeline.BossLifeReported(50);
        Timeline.BossLifeReported(40);

        Assert.Equal(40, Timeline.BossLife);
        Assert.Equal(new[] { "only:life:50", "only:life:40" }, recorder.Events);
    }

    [Fact]
    public void WritingTheBossLifeReachesTheMirrorWithoutReportingIt()
    {
        var mirror = new FakeBossMirror();
        var recorder = new Recorder([], "only");
        StartRun();
        Timeline.AttachBossMirror(mirror);
        Listen(recorder);

        Timeline.WriteBossLife(33);

        Assert.Equal(33, mirror.WrittenLife);
        Assert.Equal(33, Timeline.BossLife);
        // A write is not a change report: only what the game does to the life is reported.
        Assert.Empty(recorder.Events);

        // The game then echoing the same value back is not a change either.
        Timeline.BossLifeReported(33);
        Assert.Empty(recorder.Events);
    }

    [Fact]
    public void AnArmedBossOrderVerdictIsWrittenAndReappliedAtTheStepThatAssignsIt()
    {
        var mirror = new FakeBossMirror();
        StartRun();
        Timeline.AttachBossMirror(mirror);

        Assert.Null(Timeline.BossOrderVerdict);

        Timeline.ArmBossOrder(false);
        Assert.Equal(false, Timeline.BossOrderVerdict);
        Assert.Equal(false, mirror.Order);
        Assert.Equal(1, mirror.WriteOrderCalls);

        // The game assigns the flag at the start of the spawn loop's step, so the verdict is written again after.
        Timeline.ReapplyBossOrder();
        Assert.Equal(false, mirror.Order);
        Assert.Equal(2, mirror.WriteOrderCalls);

        // Null hands the flag back to the game: nothing is written for it any more.
        Timeline.ArmBossOrder(null);
        Timeline.ReapplyBossOrder();
        Assert.Null(Timeline.BossOrderVerdict);
        Assert.Equal(2, mirror.WriteOrderCalls);
    }

    [Fact]
    public void AVerdictArmedBeforeTheBossIsReachedLandsWhenTheMirrorArrives()
    {
        StartRun();
        Timeline.ArmBossOrder(true);
        Timeline.WriteBossLife(70);

        var mirror = new FakeBossMirror();
        Timeline.AttachBossMirror(mirror);

        Assert.Equal(true, mirror.Order);
        Assert.Equal(1, mirror.WriteOrderCalls);
        // The life write that could not land yet lands with the mirror, and lands once.
        Assert.Equal(70, mirror.WrittenLife);
        Assert.Equal(1, mirror.WriteLifeCalls);
    }

    [Fact]
    public void TheBossHandleFollowsTheRunsLookup()
    {
        StartRun();
        Assert.True(Timeline.Boss == default);

        Timeline.CaptureBoss(11);
        var boss = Timeline.Boss;
        Assert.True(boss != default);

        // The same group is the same handle, and a handle of a finished run is never a later run's.
        Timeline.CaptureBoss(11);
        Assert.Equal(boss, Timeline.Boss);

        Timeline.Reset();
        StartRun();
        Timeline.CaptureBoss(12);
        Assert.True(Timeline.Boss != boss);
    }

    [Fact]
    public void TheLeaveGateHoldsALiveRunsSceneAndOpensWithTheExitWindow()
    {
        StartRun();

        // The framework owns the scene while the run is live, so the game's own leave is held.
        Assert.False(Timeline.MayLeaveScene());

        Timeline.EndRun();

        // The run's own end is the exit window, and the switch is open by default, so the leave passes.
        Assert.True(Timeline.MayLeaveScene());

        Timeline.SetAllowLeaveScene(false);
        Assert.False(Timeline.AllowLeaveScene);
        Assert.False(Timeline.MayLeaveScene());

        // Opening the switch again re-issues the leave the gate held.
        var retried = false;
        Timeline.LeaveRetry = () => retried = true;
        Timeline.SetAllowLeaveScene(true);
        Assert.True(retried);
        Assert.True(Timeline.MayLeaveScene());
    }

    [Fact]
    public void TheExitWindowOpensWithTheChallengesOwnClose()
    {
        var retried = 0;
        StartRun();
        Timeline.LeaveRetry = () => retried++;

        Assert.False(Timeline.MayLeaveScene());

        // The game's own close is the challenge's exit: the leave passes from here on, and a leave the gate held
        // while the run was live was re-issued rather than lost.
        Timeline.ExitWindowOpened();

        Assert.True(Timeline.MayLeaveScene());
        Assert.Equal(1, retried);

        // A close outside a run belongs to another challenge: this timeline's window stays shut.
        Timeline.Reset();
        Timeline.ExitWindowOpened();
        Assert.True(Timeline.MayLeaveScene());
        StartRun();
        Assert.False(Timeline.MayLeaveScene());
    }

    [Fact]
    public void ANewRunForgetsTheBossTheVerdictAndTheLeaveWindow()
    {
        StartRun();
        Timeline.CaptureBoss(5);
        Timeline.ArmBossOrder(false);
        Timeline.SetAllowLeaveScene(false);
        Timeline.EndRun();
        Assert.False(Timeline.MayLeaveScene());

        Timeline.Reset();

        Assert.True(Timeline.Boss == default);
        Assert.Null(Timeline.BossOrderVerdict);
        Assert.True(Timeline.AllowLeaveScene);
        // With no run owned, the game's own leave is none of the framework's business.
        Assert.True(Timeline.MayLeaveScene());

        StartRun();
        Assert.False(Timeline.MayLeaveScene());
    }

    [Fact]
    public void SwallowingACookerNeedsARunAndReachesTheMirror()
    {
        var mirror = new FakeBossMirror();
        var recorder = new Recorder([], "only");
        Listen(recorder);

        // No run: the mirror is not attached and the swallow is refused rather than reaching a scene the
        // framework does not own.
        Timeline.AttachBossMirror(mirror);
        Assert.False(Timeline.SwallowCooker(3));
        Assert.Empty(mirror.Swallowed);

        StartRun();
        Timeline.AttachBossMirror(mirror);
        Assert.True(Timeline.SwallowCooker(3));
        Assert.Equal(new[] { 3 }, mirror.Swallowed);
        Assert.Equal(new[] { "only:swallowed:3" }, recorder.Events);

        // A swallow the mirror could not do is not reported at all, so a report always means the cooker was eaten.
        mirror.SwallowResult = false;
        Assert.False(Timeline.SwallowCooker(4));
        Assert.Equal(new[] { "only:swallowed:3" }, recorder.Events);
    }

    // ---- the challenge's own evaluation callbacks ----------------------------------------------------

    [Fact]
    public void EveryListenerSeesTheEvaluationAndTheRewrittenVerdictTravelsThroughThem()
    {
        StartRun();
        var order = new List<string>();
        var first = new Recorder(order, "first").RewriteEvaluation(ChallengeEvaluationResult.ExGood, true);
        var second = new Recorder(order, "second");
        var third = new Recorder(order, "third");
        Listen(first, second, third);

        var result = ChallengeEvaluationResult.Normal;
        var comboProtect = false;
        var evaluation = Timeline.InterceptBossEvaluation(11, ChallengePhase.Three, ref result, ref comboProtect, string.Empty, 1f, out var cancel);

        Assert.False(cancel);
        // Every listener is asked, each of them is handed what the previous one left behind, and what the last
        // one left behind is what the game's own callback receives.
        Assert.Equal(
            new[] { "first:pre-eval:Normal:False", "second:pre-eval:ExGood:True", "third:pre-eval:ExGood:True" },
            order);
        Assert.Equal(ChallengeEvaluationResult.ExGood, result);
        Assert.True(comboProtect);
        Assert.Equal(ChallengeEvaluationResult.ExGood, evaluation.Result);
        Assert.True(evaluation.ComboProtect);
        Assert.Equal(ChallengePhase.Three, evaluation.Phase);
        Assert.Equal(ChallengeRunKind.Story, evaluation.Kind);
        Assert.True(evaluation.Group != default);
    }

    [Fact]
    public void ACancelledEvaluationIsHeldAndIsNotReportedAsRun()
    {
        StartRun();
        var recorder = new Recorder([], "only").CancelEvaluation();
        Listen(recorder);

        var result = ChallengeEvaluationResult.Good;
        var comboProtect = false;
        var evaluation = Timeline.InterceptBossEvaluation(3, ChallengePhase.Three, ref result, ref comboProtect, string.Empty, 1f, out var cancel);

        Assert.True(cancel);
        // The values the listeners left behind are what the game goes on with in place of the callback.
        Assert.Equal(ChallengeEvaluationResult.Good, evaluation.Result);

        // The callback did not run, so the notification that reports it as run is not delivered.
        Timeline.BossEvaluated(evaluation, evaluation.Result, evaluation.ComboProtect, string.Empty, 1f, ran: false);
        Assert.Equal(new[] { "only:pre-eval:Good:False" }, recorder.Events);
    }

    [Fact]
    public void ACancelNeverHidesTheEvaluationFromTheListenersBehindIt()
    {
        StartRun();
        var order = new List<string>();
        var first = new Recorder(order, "first").CancelEvaluation();
        var second = new Recorder(order, "second");
        var third = new Recorder(order, "third");
        Listen(first, second, third);

        var result = ChallengeEvaluationResult.Bad;
        var comboProtect = false;
        Timeline.InterceptBossEvaluation(9, ChallengePhase.Three, ref result, ref comboProtect, string.Empty, 1f, out var cancel);

        Assert.True(cancel);
        // The cancel of the first listener does not hide the event from the listeners after it, and both of them
        // see the verdict it left behind.
        Assert.Equal(
            new[] { "first:pre-eval:Bad:False", "second:pre-eval:Bad:False", "third:pre-eval:Bad:False" },
            order);
        Assert.False(first.SawEvaluationCancel);
        Assert.True(second.SawEvaluationCancel);
        Assert.True(third.SawEvaluationCancel);
    }

    [Fact]
    public void ARanEvaluationIsReportedWithTheValuesTheCallbackEndedAt()
    {
        StartRun();
        var recorder = new Recorder([], "only");
        Listen(recorder);

        var result = ChallengeEvaluationResult.Normal;
        var comboProtect = false;
        var evaluation = Timeline.InterceptBossEvaluation(5, ChallengePhase.Three, ref result, ref comboProtect, string.Empty, 1f, out var cancel);
        Assert.False(cancel);

        // The listeners left Normal behind, and the game's own callback scored the group ExGood and protected
        // the combo: the report is about what the callback did, not about what it was handed.
        Timeline.BossEvaluated(evaluation, ChallengeEvaluationResult.ExGood, true, string.Empty, 1f, ran: true);

        Assert.Equal(new[] { "only:pre-eval:Normal:False", "only:eval:ExGood:True" }, recorder.Events);
    }

    [Fact]
    public void AnEvaluationOutsideARunIsLeftAlone()
    {
        var recorder = new Recorder([], "only");
        Listen(recorder);

        // No run the framework owns: another challenge's callback is not this timeline's business, so nothing is
        // asked and the values the game handed over are not touched.
        var result = ChallengeEvaluationResult.Good;
        var comboProtect = true;
        var evaluation = Timeline.InterceptBossEvaluation(7, ChallengePhase.Three, ref result, ref comboProtect, string.Empty, 1f, out var cancel);

        Assert.False(cancel);
        Assert.Empty(recorder.Events);
        Assert.Equal(ChallengeEvaluationResult.Good, result);
        Assert.True(comboProtect);
        Assert.True(evaluation == default);

        // A report is dropped for the same reason: no run was behind the callback.
        Timeline.BossEvaluated(evaluation, result, comboProtect, string.Empty, 1f, ran: true);
        Assert.Empty(recorder.Events);

        // The same holds once the run ended, even though its handles still answer.
        StartRun();
        Timeline.EndRun();
        Assert.True(Timeline.InterceptBossEvaluation(7, ChallengePhase.Three, ref result, ref comboProtect, string.Empty, 1f, out cancel) == default);
        Assert.False(cancel);
        Assert.Empty(recorder.Events);
    }

    [Fact]
    public void TheGroupHandleIsOneHandlePerGroupAndDiesWithTheRun()
    {
        StartRun();
        var result = ChallengeEvaluationResult.Normal;
        var comboProtect = false;

        var boss = Timeline.InterceptBossEvaluation(21, ChallengePhase.Three, ref result, ref comboProtect, string.Empty, 1f, out _);
        var again = Timeline.InterceptBossEvaluation(21, ChallengePhase.Three, ref result, ref comboProtect, string.Empty, 1f, out _);
        var stand = Timeline.InterceptBossEvaluation(22, ChallengePhase.Three, ref result, ref comboProtect, string.Empty, 1f, out _);

        // One group is one handle, however often it is evaluated; another group is another handle.
        Assert.True(boss.Group != default);
        Assert.Equal(boss.Group, again.Group);
        Assert.NotEqual(boss.Group, stand.Group);

        // The chain's own boss is one of the run's groups, so the boss's evaluation carries the boss's handle.
        Timeline.CaptureBoss(21);
        Assert.Equal(Timeline.Boss, boss.Group);

        // A callback the game handed no group to names no group.
        var nothing = Timeline.InterceptBossEvaluation(0, ChallengePhase.Three, ref result, ref comboProtect, string.Empty, 1f, out _);
        Assert.True(nothing.Group == default);

        // The next run drops every handle of the finished one, so a handle never means two groups.
        Timeline.Reset();
        StartRun();
        var later = Timeline.InterceptBossEvaluation(21, ChallengePhase.Three, ref result, ref comboProtect, string.Empty, 1f, out _);
        Assert.True(later.Group != boss.Group);
        Assert.True(later.Group != stand.Group);
    }

    [Fact]
    public void TheEvaluationCallbacksAreNamedByTheGameSource()
    {
        // The three are one local function of one shape declared three times, so the interop names all three the
        // same way and only the runtime name tells them apart. The ordinals are the game's own, YuyukoBossData.cs
        // 322/344 (story), 499/524 (stand) and 542/565 (retake), and each seam insists on its own.
        Assert.Equal(
            nameof(GameData.Profile.YuyukoBossData.__c__DisplayClass16_0.Method_Internal_EvaluationResult_EvaluationResult_GuestGroupController_Boolean_byref_String_byref_Boolean_0),
            ChallengeEvaluationSeams.MemberName);
        Assert.Equal("<MainChallengeLoop>g__YuyukoOverrideEvaluationCallback|33", ChallengeStoryEvaluationSeam.NativeName);
        Assert.Equal("<MainChallengeLoop>g__YuyukoOverrideEvaluationCallback|50", ChallengeRetakeEvaluationSeam.NativeName);
        Assert.Equal("<MainChallengeLoop>g__GroupOverrideEvaluationCallback|70", ChallengeStandEvaluationSeam.NativeName);
    }

    [Fact]
    public void EveryEvaluationSeamLocatesAndBindsItsCallback()
    {
        // Each of the three patches the callback of its own closure, pins that closure by the name the game's
        // source gives it, and insists, at patch time, on the name the source gives the callback: the three
        // callbacks answer to the same interop name, so only the runtime name keeps a seam off another build's
        // callback.
        Check(
            typeof(ChallengeStoryEvaluationSeam),
            "Story",
            typeof(GameData.Profile.YuyukoBossData.__c__DisplayClass16_0),
            "GameData.Profile.YuyukoBossData+<>c__DisplayClass16_0");
        Check(
            typeof(ChallengeRetakeEvaluationSeam),
            "Retake",
            typeof(GameData.Profile.YuyukoBossData.__c__DisplayClass16_6),
            "GameData.Profile.YuyukoBossData+<>c__DisplayClass16_6");
        Check(
            typeof(ChallengeStandEvaluationSeam),
            "Stand",
            typeof(GameData.Profile.YuyukoBossData.__c__DisplayClass16_9),
            "GameData.Profile.YuyukoBossData+<>c__DisplayClass16_9");

        void Check(Type seam, string patchClass, Type closure, string closureName)
        {
            // The member is resolved through NamedSeams, i.e. by the interop name and against the game's own
            // name of the callback, which is a patch time thing (it needs the IL2CPP runtime). What is checked
            // here is that the seam asks for exactly that, and that the patch class is wired to the same member.
            var located = seam.GetField("Located", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(located);
            Assert.Equal(typeof(MethodInfo), located!.FieldType);

            // The closure the seam patches is the one the interop carries the game's own name for.
            var declared = closure.GetCustomAttributesData()
                .First(attribute => attribute.AttributeType.Name == "ObfuscatedNameAttribute")
                .ConstructorArguments[0].Value as string;
            Assert.Equal(closureName, declared);

            var container = seam.GetNestedType(patchClass, BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"{seam.Name}.{patchClass} is gone.");
            var prepare = container.GetMethod("Prepare", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(prepare);
            Assert.NotNull(prepare!.GetCustomAttribute<HarmonyPrepare>());

            var patch = Assert.Single(container.GetCustomAttributes<HarmonyPatch>());
            Assert.Equal(closure, patch.info.declaringType);
            Assert.Equal(ChallengeEvaluationSeams.MemberName, patch.info.methodName);

            // Harmony binds a patch argument by name, so every argument the patch methods declare has to be an
            // argument the callback really carries (or one of Harmony's own injections).
            var target = AccessTools.Method(closure, ChallengeEvaluationSeams.MemberName);
            Assert.NotNull(target);
            BindsEveryArgument(container, "Prefix", target!);
            BindsEveryArgument(container, "Postfix", target!);
        }
    }

    [Fact]
    public void PhaseOnesSpawnLoopIsGatedLikeTheOthers()
    {
        // The phase one loop the seams hook is the one the game's own source calls Phase1GuestSpawnLoop, and it
        // is gated at its own step, the same way phase two's and the retake's third phase are.
        var container = typeof(ChallengeSpawnSeams).GetNestedType("Phase1Guests", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The phase one spawn seam is gone.");
        var patch = Assert.Single(container.GetCustomAttributes<HarmonyPatch>());
        Assert.Equal("MoveNext", patch.info.methodName);
        var source = patch.info.declaringType!.GetCustomAttributesData()
            .First(attribute => attribute.AttributeType.Name == "ObfuscatedNameAttribute")
            .ConstructorArguments[0].Value as string;
        Assert.Equal(
            "GameData.Profile.YuyukoBossData+<>c__DisplayClass16_0+<<MainChallengeLoop>g__Phase1GuestSpawnLoop|7>d",
            source);

        StartRun();
        var recorder = new Recorder([], "only").CancelSpawn(ChallengePhase.One);
        Listen(recorder);

        var attempt = Timeline.Attempt(ChallengePhase.One);
        var ran = Timeline.InterceptGuestSpawn(attempt);
        Timeline.GuestSpawned(attempt, ran);

        // Phase one is a phase of the same gate: its iteration is held and, having been held, is not reported.
        Assert.False(ran);
        Assert.Equal(new[] { "only:pre-spawn:1" }, recorder.Events);
    }

    [Fact]
    public void HarmonyAcceptsTheArgumentShapesTheEvaluationSeamsUse()
    {
        // The evaluation seams rewrite the callback's input in their prefix, write the callback's own out
        // parameters there for the callback they cancelled, decide its result from the prefix, and read an out
        // parameter of the callback by value in their postfix. Harmony has to accept all of that or the patch
        // class is never installed - and the installer only logs that - so the shapes are checked here, against
        // a target of the callback's own signature (the game is what is missing, not the shape).
        const string Owner = "dev.mystia.modding.bridge.tests.evaluation-shapes";
        var harmony = new Harmony(Owner);
        try
        {
            new PatchClassProcessor(harmony, typeof(EvaluationShapeProbe.Patch)).Patch();

            // The callback runs: the values the prefix rewrote are what it is handed, and its own result and its
            // own out parameters are what the caller gets.
            EvaluationShapeProbe.Skip = false;
            var ran = EvaluationShapeProbe.Callback(
                GuestGroupController.EvaluationResult.Normal,
                null!,
                oldComboProtect: true,
                out var ranMessage,
                out var ranComboProtect);

            Assert.Equal(GuestGroupController.EvaluationResult.ExGood, EvaluationShapeProbe.HandedResult);
            Assert.False(EvaluationShapeProbe.HandedComboProtect);
            Assert.Equal(GuestGroupController.EvaluationResult.Bad, ran);
            Assert.Equal("the game's own message", ranMessage);
            Assert.False(ranComboProtect);
            Assert.True(EvaluationShapeProbe.RanOriginal);

            // The callback is cancelled: the prefix decided its result and both of its out parameters.
            EvaluationShapeProbe.Skip = true;
            var skipped = EvaluationShapeProbe.Callback(
                GuestGroupController.EvaluationResult.Normal,
                null!,
                oldComboProtect: true,
                out var skippedMessage,
                out var skippedComboProtect);

            Assert.Equal(GuestGroupController.EvaluationResult.ExGood, skipped);
            Assert.Equal(string.Empty, skippedMessage);
            Assert.False(skippedComboProtect);
            // The state travelled from the prefix to the postfix, which saw what the prefix left behind.
            Assert.Equal(7, EvaluationShapeProbe.State);
            Assert.False(EvaluationShapeProbe.RanOriginal);
            Assert.Equal(GuestGroupController.EvaluationResult.ExGood, EvaluationShapeProbe.ReportedResult);
            Assert.False(EvaluationShapeProbe.ReportedComboProtect);
        }
        finally
        {
            Harmony.UnpatchID(Owner);
        }
    }

    // The callback's own shape, and a patch of it with the shape every evaluation seam uses.
    private static class EvaluationShapeProbe
    {
        internal static bool Skip;

        internal static int State;

        internal static bool RanOriginal;

        internal static GuestGroupController.EvaluationResult HandedResult;

        internal static bool HandedComboProtect;

        internal static GuestGroupController.EvaluationResult ReportedResult;

        internal static bool ReportedComboProtect;

        internal static GuestGroupController.EvaluationResult Callback(
            GuestGroupController.EvaluationResult lastResult,
            GuestGroupController thisGuestGroup,
            bool oldComboProtect,
            out string message,
            out bool comboProtect)
        {
            HandedResult = lastResult;
            HandedComboProtect = oldComboProtect;
            message = "the game's own message";
            comboProtect = oldComboProtect;
            return GuestGroupController.EvaluationResult.Bad;
        }

        [HarmonyPatch(typeof(EvaluationShapeProbe), nameof(Callback))]
        internal static class Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(
                ref GuestGroupController.EvaluationResult lastResult,
                GuestGroupController thisGuestGroup,
                ref bool oldComboProtect,
                out string message,
                out bool comboProtect,
                ref GuestGroupController.EvaluationResult __result,
                out int __state)
            {
                lastResult = GuestGroupController.EvaluationResult.ExGood;
                oldComboProtect = !oldComboProtect;
                message = string.Empty;
                comboProtect = false;
                // The state travelled to the postfix: there is no group to hand over in this host.
                __state = thisGuestGroup is null ? 7 : 1;
                if (!Skip)
                    return true;

                __result = GuestGroupController.EvaluationResult.ExGood;
                return false;
            }

            [HarmonyPostfix]
            private static void Postfix(
                int __state,
                GuestGroupController.EvaluationResult __result,
                bool comboProtect,
                bool __runOriginal)
            {
                State = __state;
                RanOriginal = __runOriginal;
                ReportedResult = __result;
                ReportedComboProtect = comboProtect;
            }
        }
    }

    [Fact]
    public void EveryHookedChallengeTargetExistsInThePinnedInterop() => ChallengeTargets.Verify();

    [Fact]
    public void TheBossServicesThrowOutsideTheSceneLoop()
    {
        var services = ChallengeServices.Shared;

        Assert.Throws<InvalidOperationException>(() => { _ = services.Boss; });
        Assert.Throws<InvalidOperationException>(() => { _ = services.BossLife; });
        Assert.Throws<InvalidOperationException>(() => services.BossLife = 3);
        Assert.Throws<InvalidOperationException>(() => { _ = services.BossOrderEnabled; });
        Assert.Throws<InvalidOperationException>(() => services.BossOrderEnabled = false);
        Assert.Throws<InvalidOperationException>(() => { _ = services.AllowLeaveScene; });
        Assert.Throws<InvalidOperationException>(() => services.AllowLeaveScene = false);
        Assert.Throws<InvalidOperationException>(() => services.SwallowCooker(0));
    }

    [Fact]
    public void TheBossServicesAnswerInsideTheSceneLoop()
    {
        var services = ChallengeServices.Shared;
        var mirror = new FakeBossMirror();
        ServiceScope.Enter();
        try
        {
            StartRun();
            Timeline.AttachBossMirror(mirror);
            Timeline.CaptureBoss(7);
            Timeline.BossLifeReported(60);

            Assert.True(services.Boss != default);
            Assert.Equal(60, services.BossLife);

            services.BossLife = 55;
            Assert.Equal(55, mirror.WrittenLife);
            Assert.Equal(55, services.BossLife);

            services.BossOrderEnabled = false;
            Assert.Equal(false, mirror.Order);
            Assert.Equal(false, services.BossOrderEnabled);
            services.BossOrderEnabled = null;
            Assert.Null(services.BossOrderEnabled);

            Assert.True(services.AllowLeaveScene);
            services.AllowLeaveScene = false;
            Assert.False(services.AllowLeaveScene);

            Assert.True(services.SwallowCooker(2));
            Assert.Equal(new[] { 2 }, mirror.Swallowed);
        }
        finally
        {
            ServiceScope.Exit();
        }
    }

    private void StartRun()
    {
        _phaseEnds.Clear();
        Timeline.StartRun();
        Listen(new Recorder([], "phase-ends").EndedInto(_phaseEnds));
    }

    private void Listen(params IChallengeListener[] listeners)
    {
        foreach (var listener in listeners)
            _registry.Add(listener);
    }

    // Harmony binds a patch method's arguments by name against the target's own, and a name the target does not
    // carry keeps the whole patch from installing (the installer only logs that). Every argument of every
    // challenge evaluation patch is checked here instead, against the real interop member the patch names.
    private static void BindsEveryArgument(Type container, string patchMethod, MethodInfo target)
    {
        var method = container.GetMethod(patchMethod, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{container.Name}.{patchMethod} is gone.");

        foreach (var parameter in method.GetParameters())
        {
            if (IsInjected(parameter.Name!))
                continue;

            var bound = target.GetParameters().FirstOrDefault(candidate => candidate.Name == parameter.Name);
            Assert.True(
                bound is not null,
                $"{container.Name}.{patchMethod} binds '{parameter.Name}', which the callback does not carry.");
            Assert.Equal(Plain(bound!.ParameterType), Plain(parameter.ParameterType));
        }
    }

    // The arguments Harmony injects itself, plus its positional argument names.
    private static bool IsInjected(string name) =>
        name is "__instance" or "__result" or "__state" or "__runOriginal" or "__originalMethod" or "__args"
        || name.StartsWith("___", StringComparison.Ordinal)
        || (name.Length > 2 && name[0] == '_' && name[1] == '_' && name.Skip(2).All(char.IsAsciiDigit));

    private static Type Plain(Type type) => type.IsByRef ? type.GetElementType()! : type;

    // The engine side of the boss mirror, stood in for by a plain object: everything the services and the timeline
    // do with a boss goes through this shape, so the whole mirror can be exercised without the game running.
    private sealed class FakeBossMirror : IChallengeBossMirror
    {
        internal int WrittenLife = -1;

        internal int WriteLifeCalls;

        internal bool? Order;

        internal int WriteOrderCalls;

        internal bool SwallowResult = true;

        internal List<int> Swallowed { get; } = [];

        public bool TryReadLife(out int life)
        {
            life = WrittenLife;
            return WrittenLife >= 0;
        }

        public bool WriteLife(int life)
        {
            WriteLifeCalls++;
            WrittenLife = life;
            return true;
        }

        public void WriteOrderAllowed(bool enabled)
        {
            Order = enabled;
            WriteOrderCalls++;
        }

        public bool SwallowCooker(int cookerIndex)
        {
            if (SwallowResult)
                Swallowed.Add(cookerIndex);
            return SwallowResult;
        }

        public int EarnedFund { get; set; }

        public int PositiveSpellCount { get; set; }
    }

    private sealed class Recorder : IChallengeListener
    {
        private readonly List<string> _events;
        private readonly string _name;
        private int _stepToCancel = -1;
        private float _tickToCancel = float.NaN;
        private ChallengePhase? _spawnToCancel;
        private List<ChallengeClockStop>? _phaseEnds;
        private ChallengeEvaluationResult? _rewriteEvaluation;
        private bool _rewriteComboProtect;
        private bool _cancelEvaluation;

        internal Recorder(List<string> events, string name)
        {
            _events = events;
            _name = name;
        }

        internal List<string> Events => _events;

        internal bool SawCancelOnStep { get; private set; }

        internal bool SawEvaluationCancel { get; private set; }

        internal bool Hold { get; set; }

        internal Recorder RewriteEvaluation(ChallengeEvaluationResult result, bool comboProtect)
        {
            _rewriteEvaluation = result;
            _rewriteComboProtect = comboProtect;
            return this;
        }

        internal Recorder CancelEvaluation()
        {
            _cancelEvaluation = true;
            return this;
        }

        internal Recorder CancelStep(int step)
        {
            _stepToCancel = step;
            return this;
        }

        internal Recorder CancelClockTick(float remaining)
        {
            _tickToCancel = remaining;
            return this;
        }

        internal Recorder CancelSpawn(ChallengePhase phase)
        {
            _spawnToCancel = phase;
            return this;
        }

        internal Recorder HoldClock()
        {
            Hold = true;
            return this;
        }

        internal Recorder EndedInto(List<ChallengeClockStop> stops)
        {
            _phaseEnds = stops;
            return this;
        }

        public void OnPreChallengeStep(ChallengeStep step, ref bool cancelInvocation)
        {
            _events.Add($"{_name}:pre-step");
            SawCancelOnStep = cancelInvocation;
            if (cancelInvocation || _stepToCancel != step.Index)
                return;
            cancelInvocation = true;
        }

        public void OnChallengeStepRan(ChallengeStep step, ChallengeStep next) => _events.Add($"{_name}:step-ran");

        public void OnChallengePhaseStarted(ChallengePhaseInfo phase) => _events.Add($"{_name}:phase-started");

        public void OnChallengePhaseEnded(ChallengePhase phase, ChallengeClockStop stop)
        {
            _events.Add($"{_name}:phase-ended");
            _phaseEnds?.Add(stop);
        }

        public void OnChallengeClockStarted(ChallengeClock clock) => _events.Add($"{_name}:clock-started");

        public void OnPreChallengeClockTick(ChallengeClockTick tick, ref bool cancelInvocation)
        {
            _events.Add($"{_name}:pre-clock-tick:{tick.RemainingSeconds:0}");
            if (float.IsNaN(_tickToCancel) || _tickToCancel != tick.RemainingSeconds)
                return;
            cancelInvocation = true;
        }

        public void OnChallengeClockTicked(ChallengeClockTick tick) =>
            _events.Add($"{_name}:clock-tick:{tick.RemainingSeconds:0}");

        public void OnChallengeClockElapsed(ChallengeClock clock, ref bool holdClock) => holdClock = Hold;

        public void OnPreChallengeGuestSpawn(ChallengeSpawnAttempt attempt, ref bool cancelInvocation)
        {
            _events.Add($"{_name}:pre-spawn:{attempt.Index}");
            if (_spawnToCancel == attempt.Phase)
                cancelInvocation = true;
        }

        public void OnChallengeGuestSpawned(ChallengeSpawnAttempt attempt) =>
            _events.Add($"{_name}:spawned:{attempt.Index}");

        public void OnPreBossEvaluated(ref ChallengeBossEvaluation evaluation, ref bool cancelInvocation)
        {
            _events.Add($"{_name}:pre-eval:{evaluation.Result}:{evaluation.ComboProtect}");
            SawEvaluationCancel = cancelInvocation;
            if (_rewriteEvaluation is { } result)
                evaluation = evaluation with { Result = result, ComboProtect = _rewriteComboProtect };
            if (_cancelEvaluation)
                cancelInvocation = true;
        }

        public void OnBossEvaluated(in ChallengeBossEvaluation evaluation) =>
            _events.Add($"{_name}:eval:{evaluation.Result}:{evaluation.ComboProtect}");

        public void OnChallengeFailureStarted() => _events.Add($"{_name}:failure");

        public void OnChallengeBuffEnded() => _events.Add($"{_name}:buff-ended");

        public void OnChallengeBossLifeChanged(int life) => _events.Add($"{_name}:life:{life}");

        public void OnChallengeCookerSwallowed(int cookerIndex) => _events.Add($"{_name}:swallowed:{cookerIndex}");
    }
}
