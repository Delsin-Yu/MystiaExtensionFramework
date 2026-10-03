using Mystia.Listeners;
using Mystia.Numerics;
using Mystia.Scenes;

namespace Mystia.Modding.Bridge;

/// <summary>
/// What the timeline answers one step of a phase clock with: the seam acts on the answer instead of running the
/// game's own step.
/// </summary>
internal enum ClockAction
{
    /// <summary>The game may run the piece (and the piece reports its own tick afterwards).</summary>
    Run,

    /// <summary>Hold the piece for a frame instead of running it.</summary>
    Hold,

    /// <summary>Finish the piece now, so the challenge moves on to the next step.</summary>
    End,
}

/// <summary>
/// The timeline of the running boss challenge: the run and its phase, the phase clock, the steps the challenge
/// main loop takes and the iterations of a phase's guest spawn loop, plus the listener pipeline over all four.
/// <para>
/// Nothing here names a game type. The seams translate the game's own state in and out (a resume position, the
/// seconds of the counter, the layout of a spawn loop), which is what keeps the dispatch order, the cancel
/// contract and the one shot holds of this file testable without the engine running.
/// </para>
/// </summary>
internal sealed class ChallengeTimeline
{
    /// <summary>One timeline per process: a challenge runs in the night scene alone, one at a time.</summary>
    internal static readonly ChallengeTimeline Shared = new();

    /// <summary>The phases a clock can belong to: <see cref="ChallengePhase.None"/> plus the three phases.</summary>
    private const int PhaseSlots = 4;

    private int _nextRun;
    private int _nextClock;

    private ChallengeRunHandle _run;
    private bool _running;
    private ChallengeRunKind _kind;
    private ChallengePhase _phase;
    private nint _displayer;
    private Vector3 _spawnPosition;

    private ChallengeClockHandle _clock;
    private bool _clockRunning;
    private bool _clockHeld;
    private bool _clockEndRequested;
    private bool _clockRestored;
    private float _clockSeconds;
    private float _clockRemaining;

    private readonly float[] _armedSeconds = new float[PhaseSlots];
    private readonly bool[] _armed = new bool[PhaseSlots];
    private readonly bool[] _clockRan = new bool[PhaseSlots];
    private readonly int[] _spawnCount = new int[PhaseSlots];
    private readonly ChallengeSpawnVerdict?[] _spawnVerdict = new ChallengeSpawnVerdict?[PhaseSlots];

    internal ChallengePhase Phase => _phase;

    internal ChallengeRunKind RunKind => _kind;

    internal float RemainingSeconds => _clockRunning ? _clockRemaining : -1f;

    internal ChallengeClockHandle Clock => _clockRunning ? _clock : default;

    /// <summary>
    /// Whether the phase's end is held right now: the clock reached its own stopping condition and a listener
    /// kept it from ending the phase. While this is set the clock consumes nothing and is only asked about that
    /// end again, which is why the tick gate stays out of it.
    /// </summary>
    internal bool ClockHeld => _clockHeld;

    /// <summary>Whether a challenge main loop is running, i.e. whether the framework owns a timeline at all.</summary>
    internal bool Running => _running;

    // ---- the run ------------------------------------------------------------------------------------

    /// <summary>
    /// A challenge main loop started. The previous run's phase, clock and armed values are dropped, because
    /// nothing of a finished run may steer the next one; the handles keep counting up so a handle of a
    /// finished run never equals a later one.
    /// </summary>
    internal ChallengeRunHandle StartRun()
    {
        Reset();
        _running = true;
        _run = new ChallengeRunHandle(++_nextRun);
        return _run;
    }

    /// <summary>The challenge main loop finished (or was stopped): its phase and clock are gone with it.</summary>
    internal void EndRun()
    {
        _running = false;
        _phase = ChallengePhase.None;
        _displayer = 0;
        DropClock();
        Array.Clear(_spawnVerdict);
    }

    /// <summary>The run's retake flag, read from the loop once its first step assigned it.</summary>
    internal void SetRunKind(ChallengeRunKind kind)
    {
        if (_running)
            _kind = kind;
    }

    /// <summary>
    /// The challenge's own closure, as the loop's fields show it: the status displayer it drives and the spot
    /// its phase guests spawn at. Both are read from the loop on every step, which is why they arrive together.
    /// </summary>
    internal void Attach(nint displayer, Vector3 spawnPosition)
    {
        if (!_running)
            return;
        if (displayer != 0)
            _displayer = displayer;
        _spawnPosition = spawnPosition;
    }

    /// <summary>
    /// A phase's status displayer was filled in: the challenge's own panel announces its phases with the same
    /// three values, and the first panel a run fills in is the panel of that run.
    /// </summary>
    internal void DisplayedPhase(nint displayer, ChallengePhase phase)
    {
        if (!_running || phase == ChallengePhase.None)
            return;
        if (_displayer == 0)
            _displayer = displayer;
        else if (_displayer != displayer)
            return;

        _phase = phase;
        var info = new ChallengePhaseInfo(_run, phase, _kind);
        Dispatch.Run<IChallengeListener>(listener => listener.OnChallengePhaseStarted(info));
    }

    /// <summary>
    /// Forgets everything the framework knows about a challenge. Called when a run starts and by the tests;
    /// the two handle counters deliberately survive, so a handle handed out before a reset keeps its meaning.
    /// </summary>
    internal void Reset()
    {
        _running = false;
        _phase = ChallengePhase.None;
        _kind = ChallengeRunKind.Story;
        _displayer = 0;
        _spawnPosition = default;
        DropClock();
        Array.Clear(_armed);
        Array.Clear(_clockRan);
        Array.Clear(_spawnCount);
        Array.Clear(_spawnVerdict);
    }

    // ---- the challenge main loop --------------------------------------------------------------------

    /// <summary>
    /// The challenge's main loop is about to run the step at <paramref name="resumePosition"/>. False means the
    /// loop is held there: the step does not run and the machine waits a frame at the same position.
    /// </summary>
    internal bool InterceptStep(int resumePosition)
    {
        if (!_running)
            return true;

        var step = new ChallengeStep(resumePosition);
        var cancel = false;
        foreach (var listener in Dispatch.Instances<IChallengeListener>())
            listener.OnPreChallengeStep(step, ref cancel);
        return !cancel;
    }

    /// <summary>
    /// The step at <paramref name="resumePosition"/> ran. <paramref name="ran"/> is false when the game did not
    /// run it after all (a listener or another patch held it), and the report is then dropped: the timeline only
    /// reports what the game really did.
    /// </summary>
    internal void StepRan(int resumePosition, bool ran)
    {
        if (!_running || !ran)
            return;
        var step = new ChallengeStep(resumePosition);
        Dispatch.Run<IChallengeListener>(listener => listener.OnChallengeStepRan(step));
    }

    // ---- the phase clock ----------------------------------------------------------------------------

    /// <summary>
    /// The step that starts a phase clock: the game reads its phase length from here, so this is the one place
    /// a length a mod armed is applied. Returns the length the clock must run with; the game's own length is
    /// handed back untouched when no challenge of the framework runs.
    /// </summary>
    internal float StartClock(float gameSeconds)
    {
        if (!_running)
            return gameSeconds;

        var seconds = _armed[(int)_phase] ? _armedSeconds[(int)_phase] : gameSeconds;
        _clock = new ChallengeClockHandle(++_nextClock);
        _clockRunning = true;
        _clockHeld = false;
        _clockRestored = false;
        _clockEndRequested = false;
        _clockSeconds = seconds;
        _clockRemaining = seconds;
        _clockRan[(int)_phase] = true;

        var clock = new ChallengeClock(_clock, _phase, _kind, seconds);
        Dispatch.Run<IChallengeListener>(listener => listener.OnChallengeClockStarted(clock));
        return seconds;
    }

    /// <summary>
    /// A step that is not the clock's first one. <paramref name="remaining"/> is what the game's own counter
    /// holds before the step, which is the seconds left on the clock.
    /// </summary>
    internal ClockAction ClockStep(float remaining)
    {
        if (!_clockRunning)
            return ClockAction.Run;

        _clockRemaining = NotNegative(remaining);
        if (_clockEndRequested)
        {
            _clockEndRequested = false;
            return ClockAction.End;
        }

        if (_clockHeld)
            return ClockAction.Hold;

        var cancel = false;
        foreach (var listener in Dispatch.Instances<IChallengeListener>())
            listener.OnPreChallengeClockTick(Tick(_clockRemaining), ref cancel);
        return cancel ? ClockAction.Hold : ClockAction.Run;
    }

    /// <summary>
    /// The step ran and the game's counter now reads <paramref name="remaining"/>. The counter is what the
    /// listeners see: it is the value the game itself drives, and it keeps counting when the game pauses its
    /// own time flow, which a tick count of the framework's own could not do.
    /// </summary>
    internal void ClockTicked(float remaining, bool ran)
    {
        if (!_clockRunning || !ran)
            return;

        remaining = NotNegative(remaining);
        // The game's counter is restored to the full phase length on the step that consumes the last second
        // (that is the branch which leaves its countdown loop), so a counter that grew is the one trace that
        // the clock ran out rather than being stopped by the phase's own condition. It decides the stop reason
        // reported below, and the seams never have to run the phase's own condition to find it out.
        if (remaining > _clockRemaining)
            _clockRestored = true;
        _clockRemaining = remaining;

        Dispatch.Run<IChallengeListener>(listener => listener.OnChallengeClockTicked(Tick(remaining)));
    }

    /// <summary>
    /// The clock reached the condition that stops it. Listening code may hold the phase here; true means the
    /// phase may end now, and the timeline then reports the end itself, with the reason the clock left behind.
    /// False means the phase is held: the clock waits a frame and this is asked again, which is how the hold is
    /// released later. A step held by <see cref="ClockStep"/> is asked here again on every frame for the same
    /// reason.
    /// </summary>
    internal bool StopClock()
    {
        if (!_clockRunning)
            return true;

        var clock = new ChallengeClock(_clock, _phase, _kind, _clockSeconds);
        var hold = false;
        foreach (var listener in Dispatch.Instances<IChallengeListener>())
            listener.OnChallengeClockElapsed(clock, ref hold);
        if (hold)
        {
            _clockHeld = true;
            return false;
        }

        ClockEnded(_clockRestored ? ChallengeClockStop.Elapsed : ChallengeClockStop.ObjectiveReached);
        return true;
    }

    /// <summary>
    /// The clock ended, with the reason the caller knows. The phase end is reported once per clock: a clock that
    /// already ended ignores later calls, so a routine that is finished and asked again does not report twice.
    /// The phase itself is kept, because the loops the phase started may still run for the rest of the frame.
    /// </summary>
    internal void ClockEnded(ChallengeClockStop stop)
    {
        if (!_clockRunning)
            return;

        var phase = _phase;
        DropClock();
        // A verdict armed for this phase was for the loop that just ended with it.
        Array.Clear(_spawnVerdict);
        Dispatch.Run<IChallengeListener>(listener => listener.OnChallengePhaseEnded(phase, stop));
    }

    // ---- the phase guest spawn loops ----------------------------------------------------------------

    /// <summary>The iteration of <paramref name="phase"/>'s guest spawn loop the seam is about to gate.</summary>
    internal ChallengeSpawnAttempt Attempt(ChallengePhase phase) =>
        new(phase, _spawnCount[(int)phase] + 1, _spawnPosition);

    /// <summary>
    /// The gate of one spawn iteration. A verdict armed through the services replaces the listeners' vote for
    /// the iteration it was armed for and is consumed by it; without one, a cancellation holds the iteration.
    /// Every listener is asked either way.
    /// </summary>
    internal bool InterceptGuestSpawn(in ChallengeSpawnAttempt attempt)
    {
        var armed = _spawnVerdict[(int)attempt.Phase];
        _spawnVerdict[(int)attempt.Phase] = null;

        var cancel = false;
        foreach (var listener in Dispatch.Instances<IChallengeListener>())
            listener.OnPreChallengeGuestSpawn(attempt, ref cancel);

        return armed is { } verdict ? verdict == ChallengeSpawnVerdict.Allow : !cancel;
    }

    /// <summary>
    /// The iteration ran (<paramref name="ran"/> false when it was held after all). A held iteration does not
    /// consume the number it was reported with, so the iteration that runs next keeps it.
    /// </summary>
    internal void GuestSpawned(in ChallengeSpawnAttempt attempt, bool ran)
    {
        if (!ran)
            return;

        var spawned = attempt;
        _spawnCount[(int)spawned.Phase] = spawned.Index;
        Dispatch.Run<IChallengeListener>(listener => listener.OnChallengeGuestSpawned(spawned));
    }

    // ---- what the services drive --------------------------------------------------------------------

    /// <summary>
    /// Arms the length of <paramref name="phase"/>'s clock. False when that phase's clock already ran in this
    /// run, where the length is decided and the change cannot be applied.
    /// </summary>
    internal bool ArmPhaseSeconds(ChallengePhase phase, float seconds)
    {
        if (!_running || phase == ChallengePhase.None || seconds <= 0f)
            return false;
        if (_clockRan[(int)phase])
            return false;

        _armedSeconds[(int)phase] = seconds;
        _armed[(int)phase] = true;
        return true;
    }

    /// <summary>Arms the verdict for the next iteration of <paramref name="phase"/>'s spawn loop.</summary>
    internal void ArmSpawnVerdict(ChallengePhase phase, ChallengeSpawnVerdict verdict)
    {
        if (phase == ChallengePhase.None)
            return;
        _spawnVerdict[(int)phase] = verdict;
    }

    /// <summary>Asks the running clock to stop at its next step. False when no clock runs.</summary>
    internal bool RequestClockEnd()
    {
        if (!_clockRunning)
            return false;
        _clockEndRequested = true;
        return true;
    }

    // ---- internals ---------------------------------------------------------------------------------

    private ChallengeClockTick Tick(float remaining) => new(_clock, _phase, remaining, Progress(remaining));

    /// <summary>How much of the running clock ran, the same value the game's own time callback receives.</summary>
    private float Progress(float remaining) =>
        _clockSeconds > 0f ? Math.Clamp(1f - remaining / _clockSeconds, 0f, 1f) : 1f;

    /// <summary>A counter the game drives is never negative, whatever a paused step left in it.</summary>
    private static float NotNegative(float seconds) => seconds < 0f ? 0f : seconds;

    /// <summary>Drops the running clock without reporting its end.</summary>
    private void DropClock()
    {
        _clockRunning = false;
        _clockHeld = false;
        _clockRestored = false;
        _clockEndRequested = false;
        _clockSeconds = 0f;
        _clockRemaining = 0f;
        _clock = default;
    }
}

/// <summary>
/// The work scene services of a running challenge. Like every other scene services object it acts on the
/// running scene, so it asks for the scene scope first and throws outside a scene loop.
/// </summary>
internal sealed class ChallengeServices : IWorkSceneChallengeServices
{
    internal static readonly ChallengeServices Shared = new();

    public ChallengePhase Phase
    {
        get
        {
            ServiceScope.Require();
            return ChallengeTimeline.Shared.Phase;
        }
    }

    public ChallengeRunKind RunKind
    {
        get
        {
            ServiceScope.Require();
            return ChallengeTimeline.Shared.RunKind;
        }
    }

    public float RemainingSeconds
    {
        get
        {
            ServiceScope.Require();
            return ChallengeTimeline.Shared.RemainingSeconds;
        }
    }

    public ChallengeClockHandle Clock
    {
        get
        {
            ServiceScope.Require();
            return ChallengeTimeline.Shared.Clock;
        }
    }

    public bool SetPhaseSeconds(ChallengePhase phase, float seconds)
    {
        ServiceScope.Require();
        return ChallengeTimeline.Shared.ArmPhaseSeconds(phase, seconds);
    }

    public void SetNextGuestSpawn(ChallengePhase phase, ChallengeSpawnVerdict verdict)
    {
        ServiceScope.Require();
        ChallengeTimeline.Shared.ArmSpawnVerdict(phase, verdict);
    }

    public void EndPhaseClock()
    {
        ServiceScope.Require();
        if (!ChallengeTimeline.Shared.RequestClockEnd())
            throw new InvalidOperationException("No challenge phase clock is running.");
    }
}
