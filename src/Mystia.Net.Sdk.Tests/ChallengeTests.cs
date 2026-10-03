using Mystia.Listeners;
using Mystia.Modding.Bridge;
using Mystia.Numerics;
using Mystia.Scenes;
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
        Timeline.StepRan(9, allowed);
        Assert.True(allowed);
        Assert.Equal(new[] { "ran:pre-step", "ran:step-ran" }, ran.Events);

        var held = new Recorder([], "held").CancelStep(7);
        Listen(held);
        ran.Events.Clear();
        var cancelled = Timeline.InterceptStep(7);
        Timeline.StepRan(7, cancelled);

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
        Timeline.StepRan(2, false);

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
    }

    private sealed class Recorder : IChallengeListener
    {
        private readonly List<string> _events;
        private readonly string _name;
        private int _stepToCancel = -1;
        private float _tickToCancel = float.NaN;
        private ChallengePhase? _spawnToCancel;
        private List<ChallengeClockStop>? _phaseEnds;

        internal Recorder(List<string> events, string name)
        {
            _events = events;
            _name = name;
        }

        internal List<string> Events => _events;

        internal bool SawCancelOnStep { get; private set; }

        internal bool Hold { get; set; }

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

        public void OnChallengeStepRan(ChallengeStep step) => _events.Add($"{_name}:step-ran");

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

        public void OnChallengeFailureStarted() => _events.Add($"{_name}:failure");

        public void OnChallengeBuffEnded() => _events.Add($"{_name}:buff-ended");

        public void OnChallengeBossLifeChanged(int life) => _events.Add($"{_name}:life:{life}");

        public void OnChallengeCookerSwallowed(int cookerIndex) => _events.Add($"{_name}:swallowed:{cookerIndex}");
    }
}
