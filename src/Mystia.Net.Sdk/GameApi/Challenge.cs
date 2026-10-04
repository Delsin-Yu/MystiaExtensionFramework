using Mystia;
using Mystia.Numerics;

namespace Mystia.Scenes
{
    // The challenge timeline. A boss challenge walks its phases in order; every phase owns one clock and one
    // guest spawn loop, and the challenge's own main loop advances from one resume position to the next,
    // carrying the phase data the game checks at every transition. Nothing on this surface names an engine
    // type: what it describes - the phase clock, the spawn loop and the main loop itself - are compiler
    // generated members of the game's challenge coroutines, so a mod sees a phase, a clock, a step and a
    // spawn attempt, and never the closure those live in.

    /// <summary>Which phase of a boss challenge runs.</summary>
    public enum ChallengePhase : byte
    {
        /// <summary>No phase runs: before the first one and once the challenge is over.</summary>
        None = 0,

        /// <summary>The first phase, the one whose clock is the phase one countdown.</summary>
        One = 1,

        /// <summary>The second phase, which spawns the special guests the challenge needs.</summary>
        Two = 2,

        /// <summary>The third phase, the one fought against the boss guest itself.</summary>
        Three = 3,
    }

    /// <summary>Whether the running challenge is its first attempt or its retake.</summary>
    public enum ChallengeRunKind : byte
    {
        /// <summary>The story attempt.</summary>
        Story = 0,

        /// <summary>The retake, which runs the challenge's harder third phase.</summary>
        Retake = 1,
    }

    /// <summary>Why a phase's clock stopped, i.e. why the phase stopped moving on.</summary>
    public enum ChallengeClockStop : byte
    {
        /// <summary>
        /// The clock consumed its last second while the phase's own condition was still unmet: the phase is
        /// over and the challenge checks whether it succeeded.
        /// </summary>
        Elapsed = 0,

        /// <summary>
        /// The phase's own condition ended the clock while time was left on it: the phase reached its goal.
        /// </summary>
        ObjectiveReached = 1,

        /// <summary>
        /// A mod ended the clock itself through <see cref="IWorkSceneChallengeServices.EndPhaseClock"/>.
        /// </summary>
        Ended = 2,
    }

    /// <summary>The verdict for one iteration of a phase's guest spawn loop.</summary>
    public enum ChallengeSpawnVerdict : byte
    {
        /// <summary>The loop runs the iteration and the game spawns its guests.</summary>
        Allow = 0,

        /// <summary>
        /// The iteration is held: nothing is spawned and the loop waits for its next interval, exactly as it
        /// does between two spawns.
        /// </summary>
        Hold = 1,
    }

    /// <summary>
    /// How an order a challenge evaluated scored, on the game's own scale. The values are the game's own
    /// ladder, worst first, and they are the ones a challenge's own evaluation callback is handed and hands
    /// back.
    /// </summary>
    public enum ChallengeEvaluationResult : byte
    {
        /// <summary>The order was refused: the worst verdict the game has.</summary>
        Exbad = 0,

        /// <summary>The order was bad.</summary>
        Bad = 1,

        /// <summary>The order was acceptable.</summary>
        Normal = 2,

        /// <summary>The order was good.</summary>
        Good = 3,

        /// <summary>The order was the best the game can score.</summary>
        ExGood = 4,

        /// <summary>
        /// No verdict at all: the value a challenge's own callback leaves behind when it lets its own ladder
        /// decide nothing. It is above every scored verdict, exactly as the game orders it.
        /// </summary>
        Null = 5,
    }

    // Opaque token of one challenge run. A mod compares handles and passes them back to the framework; there
    // is no public way to build one.
    /// <summary>One run of a boss challenge, i.e. one <c>MainChallengeLoop</c>.</summary>
    public readonly struct ChallengeRunHandle : IEquatable<ChallengeRunHandle>
    {
        internal ChallengeRunHandle(int id) => Id = id;

        internal int Id { get; }

        public bool Equals(ChallengeRunHandle other) => Id == other.Id;

        public override bool Equals(object? obj) => obj is ChallengeRunHandle other && Equals(other);

        public override int GetHashCode() => Id;

        public static bool operator ==(ChallengeRunHandle left, ChallengeRunHandle right) => left.Equals(right);

        public static bool operator !=(ChallengeRunHandle left, ChallengeRunHandle right) => !left.Equals(right);
    }

    // Opaque token of one phase clock, the same shape as ChallengeRunHandle.
    /// <summary>One phase clock: from the step that starts it to the step that stops it.</summary>
    public readonly struct ChallengeClockHandle : IEquatable<ChallengeClockHandle>
    {
        internal ChallengeClockHandle(int id) => Id = id;

        internal int Id { get; }

        public bool Equals(ChallengeClockHandle other) => Id == other.Id;

        public override bool Equals(object? obj) => obj is ChallengeClockHandle other && Equals(other);

        public override int GetHashCode() => Id;

        public static bool operator ==(ChallengeClockHandle left, ChallengeClockHandle right) => left.Equals(right);

        public static bool operator !=(ChallengeClockHandle left, ChallengeClockHandle right) => !left.Equals(right);
    }

    // Opaque token of one resume position of the challenge's main loop. The number is the game's own state
    // number, so a mod compares steps for equality - "hold the loop where it checks the phase data", "this is
    // the step that just ran" - and never computes with the value.
    /// <summary>One resume position of the challenge's own main loop.</summary>
    public readonly struct ChallengeStep : IEquatable<ChallengeStep>
    {
        internal ChallengeStep(int index) => Index = index;

        internal int Index { get; }

        // The steps a mod exchanges phase data at, named by what the loop does there. The numbers are the resume
        // positions of the compiled loop, audited against the pinned build's own state machine
        // (YuyukoBossData.<MainChallengeLoop>d__16.MoveNext): a mod compares the step it is handed against these
        // instead of against a number, so a build whose numbering moved fails to compile its own seam rather
        // than holding the wrong step.
        /// <summary>
        /// Phase one settled and is judged: the step stops the phase's spawn routine, clears the desk and queue
        /// registrations, and decides by the run's takings. It ends at <see cref="Phase1Failed"/> or
        /// <see cref="Phase1Story"/>.
        /// </summary>
        public static ChallengeStep Phase1Settled { get; } = new(4);

        /// <summary>Phase one's failure story, which the settle step reached.</summary>
        public static ChallengeStep Phase1Failed { get; } = new(5);

        /// <summary>The story wait after phase one succeeded.</summary>
        public static ChallengeStep Phase1Story { get; } = new(6);

        /// <summary>Phase two stopped counting: its spawn routine is stopped and its spell counter unhooked.</summary>
        public static ChallengeStep Phase2CountingStopped { get; } = new(9);

        /// <summary>Phase two settled and is judged, the way <see cref="Phase1Settled"/> judges phase one.</summary>
        public static ChallengeStep Phase2Settled { get; } = new(10);

        /// <summary>The phase three ending that prepares the retake.</summary>
        public static ChallengeStep Phase3EndingPreparingRetake { get; } = new(15);

        /// <summary>The phase three ending that ends the run.</summary>
        public static ChallengeStep Phase3Ended { get; } = new(16);

        public bool Equals(ChallengeStep other) => Index == other.Index;

        public override bool Equals(object? obj) => obj is ChallengeStep other && Equals(other);

        public override int GetHashCode() => Index;

        public static bool operator ==(ChallengeStep left, ChallengeStep right) => left.Equals(right);

        public static bool operator !=(ChallengeStep left, ChallengeStep right) => !left.Equals(right);
    }

    // Opaque token of one guest group of a challenge run. The same shape as ChallengeRunHandle: a mod compares
    // handles, there is no public way to build one.
    /// <summary>
    /// One guest group of a challenge run: the challenge's own boss, or a group the challenge itself evaluates.
    /// A mod compares handles; there is no public way to build one.
    /// </summary>
    public readonly struct ChallengeBossHandle : IEquatable<ChallengeBossHandle>
    {
        internal ChallengeBossHandle(int id) => Id = id;

        internal int Id { get; }

        public bool Equals(ChallengeBossHandle other) => Id == other.Id;

        public override bool Equals(object? obj) => obj is ChallengeBossHandle other && Equals(other);

        public override int GetHashCode() => Id;

        public static bool operator ==(ChallengeBossHandle left, ChallengeBossHandle right) => left.Equals(right);

        public static bool operator !=(ChallengeBossHandle left, ChallengeBossHandle right) => !left.Equals(right);
    }

    /// <summary>A phase that started: which phase of which run, and whether that run is the retake.</summary>
    /// <param name="Run">The run the phase belongs to.</param>
    /// <param name="Phase">The phase that started.</param>
    /// <param name="Kind">Whether the run is the story attempt or the retake.</param>
    public readonly record struct ChallengePhaseInfo(ChallengeRunHandle Run, ChallengePhase Phase, ChallengeRunKind Kind);

    /// <summary>A phase clock. <paramref name="Seconds"/> is the length the clock actually runs with, after
    /// any <see cref="IWorkSceneChallengeServices.SetPhaseSeconds"/> change was applied.</summary>
    /// <param name="Handle">The clock itself.</param>
    /// <param name="Phase">The phase whose clock this is.</param>
    /// <param name="Kind">Whether the run is the story attempt or the retake.</param>
    /// <param name="Seconds">The length of the clock in seconds.</param>
    public readonly record struct ChallengeClock(ChallengeClockHandle Handle, ChallengePhase Phase, ChallengeRunKind Kind, float Seconds);

    /// <summary>One second of a phase clock, as the game is about to consume or has just consumed it.</summary>
    /// <param name="Handle">The clock the tick belongs to.</param>
    /// <param name="Phase">The phase whose clock this is.</param>
    /// <param name="RemainingSeconds">The seconds left on the clock; never negative.</param>
    /// <param name="Progress">How much of the clock ran, from 0 (just started) to 1 (ran out).</param>
    public readonly record struct ChallengeClockTick(ChallengeClockHandle Handle, ChallengePhase Phase, float RemainingSeconds, float Progress);

    /// <summary>
    /// One iteration of a phase's guest spawn loop, as the loop is about to run it.
    /// </summary>
    /// <param name="Phase">The phase whose spawn loop this iteration belongs to.</param>
    /// <param name="Index">
    /// The iteration's number inside this phase of this run, starting at 1. A held iteration does not consume a
    /// number: the attempt that runs keeps the number the held one would have had.
    /// </param>
    /// <param name="Position">The spot the phase's guests are spawned at.</param>
    public readonly record struct ChallengeSpawnAttempt(ChallengePhase Phase, int Index, Vector3 Position);

    /// <summary>
    /// One evaluation the challenge ran on one of its own guest groups: the group, the phase it happened in,
    /// and the two values the challenge's own evaluation callback is handed - the result the game scored the
    /// order with and the combo protection flag it computed.
    /// <para>
    /// <see cref="Result"/> and <see cref="ComboProtect"/> are the callback's own inputs, and a mod may rewrite
    /// them from <see cref="IChallengeListener.OnPreBossEvaluated"/>: what it leaves behind is what the
    /// callback receives. <see cref="Message"/> and <see cref="DamageMultiplier"/> are rewritten the same way -
    /// the line the callback writes and the multiplier the story attempt keeps beside it. <see cref="Group"/>,
    /// <see cref="Phase"/> and <see cref="Kind"/> describe the evaluation and are never read back.
    /// </para>
    /// </summary>
    /// <param name="Group">
    /// The guest group under evaluation. It is the same handle <see cref="IWorkSceneChallengeServices.Boss"/>
    /// answers while that group is the challenge's own boss, and the group's own handle otherwise, so a mod can
    /// tell the boss's evaluation from a group the challenge spawned to be evaluated.
    /// </param>
    /// <param name="Phase">The phase the evaluation happened in, which is the challenge's third phase.</param>
    /// <param name="Kind">Whether the run is the story attempt or the retake.</param>
    /// <param name="Result">The result the callback is handed; a mod may rewrite it.</param>
    /// <param name="ComboProtect">The combo protection flag the callback is handed; a mod may rewrite it.</param>
    public readonly record struct ChallengeBossEvaluation(
        ChallengeBossHandle Group,
        ChallengePhase Phase,
        ChallengeRunKind Kind,
        ChallengeEvaluationResult Result,
        bool ComboProtect)
    {
        /// <summary>
        /// The line the callback writes next to its verdict, as a mod may rewrite it. A cancelled callback (see
        /// <see cref="IChallengeListener.OnPreBossEvaluated"/>) is left with exactly this line, which is how a
        /// peer replays the line the ruling machine's callback produced; the report a callback that ran comes
        /// with carries the line it ended at.
        /// </summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>
        /// The damage multiplier the challenge's own story attempt keeps beside its evaluation callback, as the
        /// callback's own input and a mod may rewrite it. It only exists for the story attempt: the retake's
        /// callbacks and the stands it spawns carry no multiplier, so the field stays at one there and writing
        /// it has no effect.
        /// </summary>
        public float DamageMultiplier { get; init; } = 1f;
    }

    /// <summary>
    /// The challenge of the running work scene: which phase runs, the time left on its clock, and the two
    /// things a mod drives from the outside - the length of a phase clock and the verdict for the next
    /// iteration of a phase's guest spawn loop.
    /// <para>
    /// Every member acts on the running scene, so every member throws outside the work scene loop's
    /// <c>Setup</c>, <c>Update</c> and <c>Shutdown</c>.
    /// </para>
    /// </summary>
    public interface IWorkSceneChallengeServices
    {
        /// <summary>The phase running now, <see cref="ChallengePhase.None"/> when no challenge phase runs.</summary>
        ChallengePhase Phase { get; }

        /// <summary>Whether the running challenge is the story attempt or the retake.</summary>
        ChallengeRunKind RunKind { get; }

        /// <summary>
        /// The seconds left on the running phase clock, or -1 when no clock runs. A clock a mod ended itself
        /// reports -1 from the moment the phase ends.
        /// </summary>
        float RemainingSeconds { get; }

        /// <summary>The running phase clock, zero when no clock runs.</summary>
        ChallengeClockHandle Clock { get; }

        /// <summary>
        /// Replaces the length of a phase's clock before that clock starts, which is how a mod stretches a
        /// phase. The game keeps one phase length for the whole challenge, so the change a phase starts with is
        /// the length every later phase inherits. Returns false when that phase's clock already ran in this
        /// challenge run, where the length can no longer be applied; the change is remembered until then, so a
        /// mod may arm it from <c>IChallengeListener.OnChallengePhaseStarted</c> or any time before.
        /// </summary>
        bool SetPhaseSeconds(ChallengePhase phase, float seconds);

        /// <summary>
        /// Sets the verdict the next iteration of <paramref name="phase"/>'s guest spawn loop uses, replacing
        /// what the listeners vote for that one iteration. The verdict is one shot: the iteration that reads it
        /// consumes it, and it does not survive the phase's end.
        /// </summary>
        void SetNextGuestSpawn(ChallengePhase phase, ChallengeSpawnVerdict verdict);

        /// <summary>
        /// Ends the running phase clock now: the clock's routine finishes and the challenge's main loop goes on
        /// to the phase's completion check. Throws <see cref="InvalidOperationException"/> when no clock runs.
        /// </summary>
        void EndPhaseClock();

        /// <summary>
        /// The challenge's own boss, i.e. the guest group the third phase is fought against, or the default
        /// handle before the run reached the boss. The handle is opaque: a mod compares it, and hands it back
        /// to whatever mod-owned code drives the boss, never to this surface.
        /// </summary>
        ChallengeBossHandle Boss { get; }

        /// <summary>
        /// The challenge's own boss group as an ordinary guest group of the entity layer. It is the same group
        /// <see cref="Boss"/> names; a mod that syncs the boss as a guest group (its orders, its servings, its
        /// evaluation) needs this handle, because every guest member speaks guest handles rather than the
        /// challenge's own. It answers none until the run reached its boss.
        /// </summary>
        GuestHandle BossGuest { get; }

        /// <summary>
        /// The boss's remaining life, mirrored from the third phase's own panel, or -1 while no life was
        /// reported. The mirror follows the game: every change the game makes to the boss's life - the panel
        /// being told the phase's context and every order the boss eats - is reported to the listeners.
        /// <para>
        /// Writing it rewrites the boss's own life and refreshes the panel with it, which is exactly what the
        /// game itself does when an order lands. A write with no boss to reach is kept and applied once the run
        /// reaches it; a write is never reported to the listeners, because only the game's own changes are.
        /// </para>
        /// </summary>
        int BossLife { get; set; }

        /// <summary>
        /// Whether the boss may order, i.e. the flag the retake's third phase assigns while its stand spawn
        /// loop runs and its order loop waits on. Null, the default, leaves the flag to the game; true or
        /// false is the verdict the framework writes into the flag at the step that assigns it, so the game's
        /// own decision for the interval that flag governs is replaced.
        /// <para>
        /// The verdict is remembered until a mod changes it or the run ends, and it has no effect outside the
        /// retake's third phase, whose spawn loop is the only thing that assigns the flag.
        /// </para>
        /// </summary>
        bool? BossOrderEnabled { get; set; }

        /// <summary>
        /// Whether the second phase's timed negative spell applies its effect. True, the default, is the game's
        /// own behaviour; false replaces the routine's body with the wait it would have ended on and tells the
        /// listeners it was suppressed.
        /// <para>
        /// A machine that takes the phase's effects from another machine needs the spell not to land twice, and
        /// the routine still has to finish rather than be stopped: the game stops the phase's routines when the
        /// phase ends, and a routine that was never started cannot be stopped.
        /// </para>
        /// </summary>
        bool TimedNegativeSpellEnabled
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>
        /// The run's takings so far, as the challenge's own loop keeps them. Reading it is how a mod sees what
        /// the phase's own steps judge by; writing it replaces that value, which is what a machine that judges
        /// by another machine's numbers needs. It answers zero before the run's own loop reached the step that
        /// keeps it.
        /// </summary>
        int EarnedFund { get; set; }

        /// <summary>The run's positive spell count so far, read and written like <see cref="EarnedFund"/>.</summary>
        int PositiveSpellCount { get; set; }

        /// <summary>
        /// Whether the challenge's scene may be left. The framework holds the game's own leave
        /// (<c>NightSceneDirector.TryLeaveSession</c>) while a challenge it owns still runs, so a leave only
        /// passes inside the challenge's exit window - the run ended and the game is on its way out. Inside
        /// that window this is the switch: true (the default) lets the leave through, false holds it and
        /// re-issues it as soon as it is set back to true. Outside the window the value is read back but has
        /// no say, because the challenge still owns the scene.
        /// </summary>
        bool AllowLeaveScene { get; set; }

        /// <summary>
        /// Swallows the cooker at <paramref name="cookerIndex"/> - an index into the cooker desks the scene
        /// has - the way the challenge's own swallow does it: the cooking at that desk is interrupted, the
        /// desk is hidden and locked for the rest of the boss buff. This is the replay a peer that was told
        /// which cooker the boss ate runs, and it reports the swallow to the listeners like the game's own one
        /// does.
        /// <para>
        /// Returns false when no boss run is mirrored or the index names no cooker desk. Swallowing a desk
        /// twice is harmless and reports nothing the second time.
        /// </para>
        /// </summary>
        bool SwallowCooker(int cookerIndex);
    }
}

namespace Mystia.Listeners
{
    using Mystia.Scenes;

    /// <summary>
    /// The challenge timeline of the work scene: phase starts and ends, the phase clock, the main loop's own
    /// steps, the iterations of a phase's guest spawn loop, and the challenge's own evaluation callbacks.
    /// <para>
    /// The <c>OnPre…</c> members are interceptions: every listener is asked, so a cancellation never hides the
    /// event from the listeners registered after it, and what cancels reaches the framework once all of them
    /// ran. A cancellation holds the piece of the timeline the callback belongs to - the main loop step, the
    /// clock's second, the spawn iteration or the challenge's own evaluation callback - instead of letting the
    /// game run it; the notification that reports that piece as done is then not delivered, because the game
    /// did not do it.
    /// </para>
    /// </summary>
    [AutoWire]
    public interface IChallengeListener
    {
        /// <summary>
        /// The challenge's main loop is about to run the step at <paramref name="step"/>. Cancelling holds the
        /// loop at that same resume position and lets it wait a frame, which is how the phase's data is checked
        /// again before the game goes on.
        /// </summary>
        void OnPreChallengeStep(ChallengeStep step, ref bool cancelInvocation) { }

        /// <summary>
        /// The step at <paramref name="step"/> ran and the loop moved on to <paramref name="next"/> - which is
        /// the step it will resume at, or the step it is waiting inside when the step started a routine of its
        /// own. A mod that follows what a step settled watches the pair: phase one's settle step is the one
        /// that ends at <see cref="ChallengeStep.Phase1Failed"/> or <see cref="ChallengeStep.Phase1Story"/>.
        /// </summary>
        void OnChallengeStepRan(ChallengeStep step, ChallengeStep next) { }

        /// <summary>A phase of the running challenge started, before that phase's clock starts.</summary>
        void OnChallengePhaseStarted(ChallengePhaseInfo phase) { }

        /// <summary>
        /// A phase ended, i.e. its clock stopped. The game's own completion check for the phase runs right
        /// after this, which is what decides whether the phase was a success.
        /// </summary>
        void OnChallengePhaseEnded(ChallengePhase phase, ChallengeClockStop stop) { }

        /// <summary>
        /// A phase clock started. <paramref name="clock"/> carries the length the clock runs with, after any
        /// change a mod armed through <c>IWorkSceneChallengeServices.SetPhaseSeconds</c>. The first second is
        /// consumed inside the step this is reported from.
        /// </summary>
        void OnChallengeClockStarted(ChallengeClock clock) { }

        /// <summary>
        /// The next second of the clock is about to be consumed. Cancelling holds the clock for a frame: the
        /// second is not consumed and the clock is asked again instead of moving on.
        /// </summary>
        void OnPreChallengeClockTick(ChallengeClockTick tick, ref bool cancelInvocation) { }

        /// <summary>The second reported by <paramref name="tick"/> was consumed.</summary>
        void OnChallengeClockTicked(ChallengeClockTick tick) { }

        /// <summary>
        /// The clock reached its own stopping condition - the phase's goal was met, or the last second was
        /// consumed. Writing true into <paramref name="holdClock"/> keeps the phase from ending: the clock
        /// waits and is asked again, so a mod that waits for another peer's decision releases it later by
        /// leaving the value false, and it also has <c>EndPhaseClock</c> to end the clock itself.
        /// </summary>
        void OnChallengeClockElapsed(ChallengeClock clock, ref bool holdClock) { }

        /// <summary>
        /// One iteration of <paramref name="attempt"/>'s phase spawn loop is about to run: the loop picks the
        /// guests of this phase and hands them to the scene. Cancelling holds the iteration - nothing is spawned
        /// and the loop waits for its next interval - which is what a peer that spawns its own guests does.
        /// Cancelling here only holds the spawn; it never stops the loop itself.
        /// </summary>
        void OnPreChallengeGuestSpawn(ChallengeSpawnAttempt attempt, ref bool cancelInvocation) { }

        /// <summary>The iteration reported by <paramref name="attempt"/> ran and spawned its guests.</summary>
        void OnChallengeGuestSpawned(ChallengeSpawnAttempt attempt) { }

        /// <summary>
        /// The second phase's timed negative spell was suppressed, and the routine is finishing on the wait it
        /// would have ended on. This is where a mod tells the player what the game's own line would have said,
        /// since the framework has no text of its own to show.
        /// </summary>
        void OnTimedNegativeSpellSuppressed() { }

        /// <summary>
        /// The challenge let the game's own leave of the night scene through: the run is over and the scene is
        /// on its way out. It fires before the game loads the next scene inside that leave, so a mod that reads
        /// its own scene transition decisions while the load is under way sees what this sets.
        /// </summary>
        void OnChallengeLeaveStarted() { }

        /// <summary>The leave the challenge let through returned.</summary>
        void OnChallengeLeaveFinished() { }

        /// <summary>
        /// The challenge's own evaluation callback for <paramref name="evaluation"/>'s group is about to run,
        /// with the result and the combo protection flag the game hands it. A mod may rewrite both
        /// (<c>evaluation = evaluation with { Result = … }</c>), and the callback then runs with what the
        /// listeners left behind, so the value reaches the challenge's own ladder and its own side effects
        /// without the mod knowing what those are.
        /// <para>
        /// Cancelling keeps the challenge's own callback from running at all: its scoring and its side effects -
        /// the life a boss loses over the group, the guests it tells to stop ordering, the cookers it locks -
        /// are skipped, and the game goes on with the values the listeners left behind. This is a peer that
        /// replays the ruling it was told, or an authority that decides the evaluation itself.
        /// </para>
        /// </summary>
        void OnPreBossEvaluated(ref ChallengeBossEvaluation evaluation, ref bool cancelInvocation) { }

        /// <summary>
        /// The challenge's own evaluation callback behind <paramref name="evaluation"/> ran. The result and the
        /// combo protection flag are the ones the callback ended at - the game's own verdict where it overrode
        /// the values it was handed - so a mod that mirrors the challenge elsewhere follows what it really did.
        /// Not delivered when a listener cancelled the callback, because the callback did not run.
        /// </summary>
        void OnBossEvaluated(in ChallengeBossEvaluation evaluation) { }

        /// <summary>
        /// The running challenge's failure story started: the game has disabled the player's panels, cleared
        /// the counted and timed buffs and told the scheduler the challenge failed, and is about to wait for
        /// that failure to be acknowledged. The failure close - the izakaya closed with the challenge's own
        /// close type - runs after the wait, never before it.
        /// </summary>
        void OnChallengeFailureStarted() { }

        /// <summary>
        /// The retake's boss buff ended, i.e. the game ran its own cleanup for that buff: the order rate
        /// modifier it added was taken back, the cookers the boss swallowed were unlocked again and their
        /// effects destroyed. This is the retake's third phase only, and the notification is a report: the
        /// cleanup happened already.
        /// </summary>
        void OnChallengeBuffEnded() { }

        /// <summary>
        /// The boss's life changed, as the third phase's panel was told to show it. The value is the new life,
        /// so a listener that mirrors the boss elsewhere can follow the game.
        /// </summary>
        void OnChallengeBossLifeChanged(int life) { }

        /// <summary>
        /// The boss swallowed the cooker at <paramref name="cookerIndex"/> (an index into the cooker desks the
        /// scene has), which is the game's own swallow only: a swallow a mod ran through
        /// <c>IWorkSceneChallengeServices.SwallowCooker</c> is not hidden from the listeners, but it is
        /// reported by the same callback. The lock the swallow takes is released when the boss buff ends.
        /// </summary>
        void OnChallengeCookerSwallowed(int cookerIndex) { }
    }
}
