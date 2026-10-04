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
/// The engine side of the boss mirror: the running challenge's own closure, as the seams reached it. The
/// services speak in values - a life, a verdict, a cooker index - and the seam translates them to whatever the
/// game holds; nothing here names an engine type, so the timeline can hold one without naming one either.
/// </summary>
internal interface IChallengeBossMirror
{
    /// <summary>The boss's life as the game holds it. False when the boss holds none yet.</summary>
    bool TryReadLife(out int life);

    /// <summary>
    /// Writes the boss's life and refreshes the panel with it, the same two steps the game takes when an
    /// order lands. False when the boss cannot be reached.
    /// </summary>
    bool WriteLife(int life);

    /// <summary>Writes the retake's order flag. Does nothing where no such flag lives (the story attempt).</summary>
    void WriteOrderAllowed(bool enabled);

    /// <summary>Eats the cooker at <paramref name="cookerIndex"/> at the cooker layer. False when impossible.</summary>
    bool SwallowCooker(int cookerIndex);

    /// <summary>
    /// The run's takings so far, as the closure the boss's life lives in keeps them. Reading it is how a mod
    /// sees what the run's own steps judge by; writing it replaces that value, which a machine judging by
    /// another machine's numbers needs.
    /// </summary>
    int EarnedFund { get; set; }

    /// <summary>The run's positive spell count so far, read and written like <see cref="EarnedFund"/>.</summary>
    int PositiveSpellCount { get; set; }
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
    private int _nextGroup;

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

    private nint _boss;
    private ChallengeBossHandle _bossHandle;
    private GuestHandle _bossGuest;
    private readonly Dictionary<nint, ChallengeBossHandle> _groups = [];
    private bool? _bossOrderEnabled;
    private bool? _negativeSpellEnabled;
    private int _bossLife = -1;
    private bool _bossLifeKnown;
    private bool _bossLifePending;
    private bool _exitWindowOpen;
    private bool _allowLeave = true;

    internal ChallengePhase Phase => _phase;

    internal ChallengeRunKind RunKind => _kind;

    internal float RemainingSeconds => _clockRunning ? _clockRemaining : -1f;

    internal ChallengeClockHandle Clock => _clockRunning ? _clock : default;

    /// <summary>The length the challenge's own data gives one phase; zero until the run reached that data.</summary>
    internal float BasePhaseSeconds { get; private set; }

    /// <summary>The engine side of the boss mirror; null until the run's own closure was reached.</summary>
    internal IChallengeBossMirror? BossMirror;

    /// <summary>
    /// Re-issues a leave the gate held, installed by the leave seam so the switch can release a leave that
    /// arrived while it was still closed. Null until the seam saw its first leave.
    /// </summary>
    internal Action? LeaveRetry;

    /// <summary>The boss the run reached, default before its third phase looked the boss up.</summary>
    internal ChallengeBossHandle Boss => _bossHandle;

    /// <summary>The framework's verdict for the boss's order flag, or null while the game owns it.</summary>
    internal bool? BossOrderVerdict => _bossOrderEnabled;

    /// <summary>The framework's verdict for the second phase's timed negative spell, or null while the game owns it.</summary>
    internal bool? NegativeSpellVerdict => _negativeSpellEnabled;

    /// <summary>
    /// The boss's life: what the game reported last, or what a mod wrote, or -1 while nothing did. The mirror
    /// is read live when the boss can be reached, so a change the game made without telling the panel is seen
    /// too.
    /// </summary>
    internal int BossLife
    {
        get
        {
            if (BossMirror is { } mirror && mirror.TryReadLife(out var life))
                return life;
            return _bossLife;
        }
    }

    /// <summary>Whether the challenge's scene may be left right now (the services' switch, gated by the window).</summary>
    internal bool AllowLeaveScene => _allowLeave;

    /// <summary>
    /// Whether the game may leave the scene now. While a run the framework owns still owns the scene the leave
    /// is held, and the switch only has a say inside the exit window, which the run's own end opens.
    /// </summary>
    internal bool MayLeaveScene() => !OwnsScene || (_exitWindowOpen && _allowLeave);

    /// <summary>Whether the framework owns the scene: a run is live, or its exit window is still open.</summary>
    private bool OwnsScene => _running || _exitWindowOpen;

    /// <summary>
    /// Opens the exit window, which is the challenge's own way out: a leave the gate held while the challenge was
    /// live is released at once, because the challenge's exit is exactly what the hold waited for and losing the
    /// game's own leave would leave the scene hanging. A mod that keeps the switch shut keeps holding it, and the
    /// retry is the seam's, so the leave is re-issued where re-entering the scene machinery is safe.
    /// </summary>
    private void OpenExitWindow()
    {
        _exitWindowOpen = true;
        if (_allowLeave)
            LeaveRetry?.Invoke();
    }

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
        BossMirror = null;
        DropClock();
        Array.Clear(_spawnVerdict);
        // The run is over, which is the challenge's exit window: the game goes on to close the izakaya and leave
        // the scene, and only from here on may the leave gate let a leave through.
        OpenExitWindow();
    }

    /// <summary>
    /// The challenge's own exit began - the game started closing the izakaya for the challenge. The exit window
    /// opens here as well as at the run's end, because the game may ask to leave the scene inside the step that
    /// starts the close, before the loop ever returns.
    /// </summary>
    internal void ExitWindowOpened()
    {
        if (!_running)
            return;
        OpenExitWindow();
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
    /// The length the run's own data gives one phase, which is what <c>SetPhaseSeconds</c> replaces and what a
    /// mod scales when it wants a phase longer. Read from the run's data on every step, so it is zero until the
    /// loop reached it.
    /// </summary>
    internal void AttachPhaseLength(int seconds)
    {
        if (!_running || seconds <= 0)
            return;
        BasePhaseSeconds = seconds;
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
        BasePhaseSeconds = 0f;
        _boss = 0;
        _bossHandle = default;
        _groups.Clear();
        _bossOrderEnabled = null;
        _negativeSpellEnabled = null;
        _bossLife = -1;
        _bossLifeKnown = false;
        _bossLifePending = false;
        _exitWindowOpen = false;
        _allowLeave = true;
        BossMirror = null;
        LeaveRetry = null;
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
    /// The step at <paramref name="resumePosition"/> ran and the loop moved on to <paramref name="nextPosition"/>.
    /// <paramref name="ran"/> is false when the game did not run it after all (a listener or another patch held
    /// it), and the report is then dropped: the timeline only reports what the game really did.
    /// </summary>
    internal void StepRan(int resumePosition, int nextPosition, bool ran)
    {
        if (!_running || !ran)
            return;
        var step = new ChallengeStep(resumePosition);
        var next = new ChallengeStep(nextPosition);
        Dispatch.Run<IChallengeListener>(listener => listener.OnChallengeStepRan(step, next));
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

    // ---- the boss -----------------------------------------------------------------------------------

    /// <summary>Whether the panel at <paramref name="displayer"/> is this run's own status panel.</summary>
    internal bool IsStatusPanel(nint displayer) => _displayer != 0 && _displayer == displayer;

    /// <summary>
    /// The run's own closure was reached, so the boss lives in it from now on. What a mod armed before the boss
    /// existed is written into the fresh mirror at once - the order verdict and a life write that could not land
    /// yet - because the run seam reaches the closure on every one of its steps.
    /// </summary>
    internal void AttachBossMirror(IChallengeBossMirror mirror)
    {
        if (!_running)
            return;
        BossMirror = mirror;
        if (_bossOrderEnabled is { } verdict)
            mirror.WriteOrderAllowed(verdict);
        if (_bossLifePending)
        {
            mirror.WriteLife(_bossLife);
            _bossLifePending = false;
        }
    }

    /// <summary>
    /// The run looked its boss up, which the challenge does with the same lookup a mod would: the controlled
    /// guest group of the run. The handle counts up, so a handle of a finished run never equals a later one,
    /// and it stays valid until the next run starts.
    /// </summary>
    internal void CaptureBoss(nint pointer, GuestHandle bossGuest = default)
    {
        if (!_running || pointer == 0)
            return;
        // The boss is one of the run's guest groups, so it carries the very handle an evaluation of it carries,
        // and a mod that syncs the boss as an ordinary guest group needs that handle rather than the challenge's
        // own.
        _boss = pointer;
        _bossHandle = HandleFor(pointer);
        _bossGuest = bossGuest;
    }

    /// <summary>The boss group as the entity layer names it, for a mod that drives the boss as a guest group.</summary>
    internal GuestHandle BossGuest => _bossGuest;

    /// <summary>
    /// The handle of one guest group of the run: the same handle for the same group for as long as the run is
    /// known, and a fresh one for a group the run has not been asked about. Zero has no handle, because a
    /// callback the game never handed a group to is evaluating nothing.
    /// </summary>
    internal ChallengeBossHandle HandleFor(nint group)
    {
        if (group == 0)
            return default;
        if (_groups.TryGetValue(group, out var handle))
            return handle;

        handle = new ChallengeBossHandle(++_nextGroup);
        _groups[group] = handle;
        return handle;
    }

    /// <summary>
    /// One of the challenge's own evaluation callbacks is about to run: <paramref name="group"/> is the group it
    /// evaluates, and <paramref name="result"/> and <paramref name="comboProtect"/> are the values the game
    /// hands it, which the listeners see and may rewrite and which are written back here. The returned value is
    /// what the listeners were shown; <paramref name="cancel"/> is true when the callback must not run at all,
    /// and the values the listeners left behind are then what stands in for it.
    /// <para>
    /// A run the framework does not own is left alone: nothing is asked, nothing is written back and no handle
    /// is handed out, so another challenge's callback is not touched.
    /// </para>
    /// </summary>
    internal ChallengeBossEvaluation InterceptBossEvaluation(
        nint group,
        ChallengePhase phase,
        ref ChallengeEvaluationResult result,
        ref bool comboProtect,
        string message,
        float damageMultiplier,
        out bool cancel)
    {
        cancel = false;
        if (!_running)
            return default;

        var evaluation = new ChallengeBossEvaluation(HandleFor(group), phase, _kind, result, comboProtect)
        {
            // The callback's own line and the story attempt's multiplier are its inputs on the way in only
            // when whoever calls this already knows them - a replay writes the ruling machine's values here.
            Message = message,
            DamageMultiplier = damageMultiplier,
        };
        foreach (var listener in Dispatch.Instances<IChallengeListener>())
            listener.OnPreBossEvaluated(ref evaluation, ref cancel);

        result = evaluation.Result;
        comboProtect = evaluation.ComboProtect;
        return evaluation;
    }

    /// <summary>
    /// The evaluation callback behind <paramref name="evaluation"/> ran, and <paramref name="result"/> and
    /// <paramref name="comboProtect"/> are the values it ended at - the game's own verdict where its callback
    /// overrode what it was handed. <paramref name="ran"/> false - a listener cancelled the callback - drops
    /// the report, because the callback the report is about did not run.
    /// </summary>
    internal void BossEvaluated(
        in ChallengeBossEvaluation evaluation,
        ChallengeEvaluationResult result,
        bool comboProtect,
        string message,
        float damageMultiplier,
        bool ran)
    {
        if (!_running || !ran)
            return;
        var final = evaluation with
        {
            Result = result,
            ComboProtect = comboProtect,
            Message = message,
            DamageMultiplier = damageMultiplier,
        };
        Dispatch.Run<IChallengeListener>(listener => listener.OnBossEvaluated(final));
    }

    /// <summary>
    /// Arms the framework's verdict for the boss's order flag. Null leaves the flag to the game again, which
    /// means the flag keeps whatever the game last assigned. The verdict is written into the retake's flag
    /// where one lives, both now and at every step of the loop that assigns it.
    /// </summary>
    internal void ArmBossOrder(bool? enabled)
    {
        _bossOrderEnabled = enabled;
        if (enabled is { } verdict)
            BossMirror?.WriteOrderAllowed(verdict);
    }

    /// <summary>
    /// Arms the framework's verdict for the second phase's timed negative spell. Null leaves the routine to the
    /// game; false is read by the routine's own seam, which turns its body into the wait it would have ended on.
    /// </summary>
    internal void ArmNegativeSpell(bool enabled) => _negativeSpellEnabled = enabled;

    /// <summary>The run's takings so far, through the closure the attached mirror holds.</summary>
    internal int EarnedFund
    {
        get => BossMirror?.EarnedFund ?? 0;
        set
        {
            if (BossMirror is { } mirror)
                mirror.EarnedFund = value;
        }
    }

    /// <summary>The run's positive spell count so far, through the closure the attached mirror holds.</summary>
    internal int PositiveSpellCount
    {
        get => BossMirror?.PositiveSpellCount ?? 0;
        set
        {
            if (BossMirror is { } mirror)
                mirror.PositiveSpellCount = value;
        }
    }

    /// <summary>
    /// Writes the armed verdict into the retake's order flag again, called from the step that assigns the flag:
    /// the game assigns it at the start of that step, so writing here is what makes the framework's verdict the
    /// one the flag holds for the interval it governs. A null verdict leaves the game's own assignment alone.
    /// </summary>
    internal void ReapplyBossOrder()
    {
        if (_bossOrderEnabled is { } verdict)
            BossMirror?.WriteOrderAllowed(verdict);
    }

    /// <summary>
    /// Writes the boss's life the way the game does. The write reaches the boss when it is reachable, and is
    /// otherwise kept and landed as soon as the run reaches it. A write is not a change report: only what the
    /// game itself does to the life is reported to the listeners.
    /// </summary>
    internal void WriteBossLife(int life)
    {
        _bossLife = life;
        _bossLifeKnown = true;
        if (BossMirror is { } mirror)
        {
            mirror.WriteLife(life);
            _bossLifePending = false;
        }
        else
        {
            _bossLifePending = true;
        }
    }

    /// <summary>
    /// The game changed the boss's life. Reported once per value, and only while a run is live: the panel
    /// reports both the context it is told and every progress it is handed, and the two can repeat.
    /// </summary>
    internal void BossLifeReported(int life)
    {
        if (!_running)
            return;
        if (_bossLifeKnown && _bossLife == life)
            return;
        _bossLife = life;
        _bossLifeKnown = true;
        Dispatch.Run<IChallengeListener>(listener => listener.OnChallengeBossLifeChanged(life));
    }

    /// <summary>The run's failure story started; the failure close runs after it, once the story is over.</summary>
    internal void FailureStarted()
    {
        if (!_running)
            return;
        Dispatch.Run<IChallengeListener>(listener => listener.OnChallengeFailureStarted());
    }

    /// <summary>
    /// The retake's boss buff ended, which is the game's own cleanup for it: the cookers the framework
    /// swallowed are unlocked the same way the game unlocks its own, and the cleanup is reported.
    /// </summary>
    internal void BuffEnded()
    {
        if (!_running)
            return;
        Dispatch.Run<IChallengeListener>(listener => listener.OnChallengeBuffEnded());
    }

    /// <summary>The boss swallowed a cooker: reported, and the framework's mirror never hides a swallow.</summary>
    internal void CookerSwallowed(int cookerIndex)
    {
        if (!_running)
            return;
        Dispatch.Run<IChallengeListener>(listener => listener.OnChallengeCookerSwallowed(cookerIndex));
    }

    /// <summary>
    /// Eats a cooker at the cooker layer. False when no boss is mirrored or the index is not a desk. A swallow
    /// that happened is reported to the listeners, so a mod cannot tell the game's own swallow from the replay.
    /// </summary>
    internal bool SwallowCooker(int cookerIndex)
    {
        if (BossMirror is not { } mirror || !mirror.SwallowCooker(cookerIndex))
            return false;
        CookerSwallowed(cookerIndex);
        return true;
    }

    /// <summary>Sets the leave switch. Opening it re-issues a leave the gate held while it was shut.</summary>
    internal void SetAllowLeaveScene(bool allow)
    {
        _allowLeave = allow;
        if (allow)
            LeaveRetry?.Invoke();
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

    public float BasePhaseSeconds
    {
        get
        {
            ServiceScope.Require();
            return ChallengeTimeline.Shared.BasePhaseSeconds;
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

    public GuestHandle BossGuest
    {
        get
        {
            ServiceScope.Require();
            return ChallengeTimeline.Shared.BossGuest;
        }
    }

    public ChallengeBossHandle Boss
    {
        get
        {
            ServiceScope.Require();
            return ChallengeTimeline.Shared.Boss;
        }
    }

    public int BossLife
    {
        get
        {
            ServiceScope.Require();
            return ChallengeTimeline.Shared.BossLife;
        }
        set
        {
            ServiceScope.Require();
            ChallengeTimeline.Shared.WriteBossLife(value);
        }
    }

    public bool? BossOrderEnabled
    {
        get
        {
            ServiceScope.Require();
            return ChallengeTimeline.Shared.BossOrderVerdict;
        }
        set
        {
            ServiceScope.Require();
            ChallengeTimeline.Shared.ArmBossOrder(value);
        }
    }

    public bool TimedNegativeSpellEnabled
    {
        get
        {
            ServiceScope.Require();
            return ChallengeTimeline.Shared.NegativeSpellVerdict is not false;
        }
        set
        {
            ServiceScope.Require();
            ChallengeTimeline.Shared.ArmNegativeSpell(value);
        }
    }

    public int EarnedFund
    {
        get
        {
            ServiceScope.Require();
            return ChallengeTimeline.Shared.EarnedFund;
        }
        set
        {
            ServiceScope.Require();
            ChallengeTimeline.Shared.EarnedFund = value;
        }
    }

    public int PositiveSpellCount
    {
        get
        {
            ServiceScope.Require();
            return ChallengeTimeline.Shared.PositiveSpellCount;
        }
        set
        {
            ServiceScope.Require();
            ChallengeTimeline.Shared.PositiveSpellCount = value;
        }
    }

    public bool AllowLeaveScene
    {
        get
        {
            ServiceScope.Require();
            return ChallengeTimeline.Shared.AllowLeaveScene;
        }
        set
        {
            ServiceScope.Require();
            ChallengeTimeline.Shared.SetAllowLeaveScene(value);
        }
    }

    public bool SwallowCooker(int cookerIndex)
    {
        ServiceScope.Require();
        return ChallengeTimeline.Shared.SwallowCooker(cookerIndex);
    }
}
