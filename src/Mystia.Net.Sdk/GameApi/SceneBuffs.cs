namespace Mystia.Scenes;

/// <summary>
/// Timed buffs registered against a buff id declared through <c>IDatabaseExtension.OnInjectBuffs</c>.
/// Durations are seconds of work time; a buff id already registered by the game is extended rather than
/// registered twice.
/// </summary>
public interface IWorkSceneBuffs
{
    /// <summary>
    /// Registers (or extends) a timed buff. <paramref name="description"/> rewrites the buff description
    /// while it runs (<c>currentSeconds</c>, <c>description</c>) and <paramref name="onBuffEnd"/> runs when
    /// the buff ends. Returns false when the buff id is not declared.
    /// </summary>
    bool RegisterTimedBuff(int buffType, int durationSeconds, Action? onBuffEnd = null, Func<int, string, string>? description = null, bool isPositive = true);

    bool HasTimedBuff(int buffType);

    void ExtendTimedBuff(int buffType, int extraSeconds);

    /// <summary>
    /// Caps the evaluation of guests whose order does (or, with <paramref name="containsOrNot"/> false, does
    /// not) carry one of <paramref name="tags"/> at <paramref name="maxEvaluation"/> for the duration.
    /// </summary>
    void LimitEvalLevel(int buffType, int durationSeconds, int maxEvaluation, IReadOnlyList<int>? tags, bool food, bool containsOrNot, Action? onBuffEnd = null, Func<int, string, string>? description = null);
}

/// <summary>The spell a work scene is currently executing: which spell, in whose favour.</summary>
public interface ISpellHost
{
    int SpellId { get; }

    /// <summary>The guest the spell is cast on, or -1 when it is not aimed at a guest.</summary>
    int GuestId { get; }
}

/// <summary>Handle of a playing effect; <see cref="Stop"/> ends it early.</summary>
public interface IVfxHandle
{
    void Stop();
}
