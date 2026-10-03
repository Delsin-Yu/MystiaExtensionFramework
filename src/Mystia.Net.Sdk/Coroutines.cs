using System.Collections;

namespace Mystia;

// Opaque token. A mod compares handles and passes them to Stop; there is no public way to build one.
public readonly struct CoroutineHandle : IEquatable<CoroutineHandle>
{
    internal CoroutineHandle(int id) => Id = id;

    internal int Id { get; }

    public bool Equals(CoroutineHandle other) => Id == other.Id;

    public override bool Equals(object? obj) => obj is CoroutineHandle other && Equals(other);

    public override int GetHashCode() => Id;

    public static bool operator ==(CoroutineHandle left, CoroutineHandle right) => left.Equals(right);

    public static bool operator !=(CoroutineHandle left, CoroutineHandle right) => !left.Equals(right);
}

/// <summary>
/// Opaque host of a coroutine, i.e. the thing a routine's lifetime is tied to.
/// <para>
/// A routine started with <see cref="ICoroutineDispatcher.StartOn"/> runs while the owner it was started on
/// is alive and is stopped before its next step once the owner is gone, exactly like a Unity coroutine whose
/// <c>MonoBehaviour</c> was destroyed. A mod receives owners from the framework — in particular from
/// <see cref="ICoroutineDispatcher.Owner"/> — compares them by reference and passes them back to
/// <c>StartOn</c>; there is no public way to build one, and an owner the framework did not hand out owns
/// nothing, so a routine started on such a value never runs.
/// </para>
/// </summary>
public interface ICoroutineOwner
{
}

/// <summary>
/// Opaque yield token. Yield it from a routine started through <see cref="ICoroutineDispatcher"/> and the
/// framework decides when the routine is resumed; how a token is described is internal to the framework.
/// </summary>
public readonly struct CoroutineAwait
{
    internal enum WaitKind : byte
    {
        NextFrame,
        Seconds,
        SecondsRealtime,
        FixedUpdate,
        EndOfFrame,
        Until,
        While,
        Nested,
    }

    internal CoroutineAwait(WaitKind kind, float seconds = 0f, Func<bool>? condition = null, Func<ICoroutineDispatcher, IEnumerator>? routine = null)
    {
        Kind = kind;
        Seconds = seconds;
        Condition = condition;
        Routine = routine;
    }

    internal WaitKind Kind { get; }

    internal float Seconds { get; }

    internal Func<bool>? Condition { get; }

    internal Func<ICoroutineDispatcher, IEnumerator>? Routine { get; }
}

/// <summary>
/// Runs mod coroutines on the game's main thread. One dispatcher owns the routines started through it, so
/// <see cref="StopAll"/> stays inside one unit of work.
/// <para>
/// A routine a mod hands to <see cref="Start"/> or <see cref="StartOn"/> is a plain <see cref="IEnumerator"/>
/// and may only yield <c>null</c> (next frame) or one of the await tokens this interface exposes:
/// <see cref="NextFrame"/>, <see cref="AfterSeconds"/>, <see cref="AfterSecondsRealtime"/>,
/// <see cref="AfterFixedUpdate"/>, <see cref="AtEndOfFrame"/>, <see cref="Until"/>, <see cref="While"/> and
/// <see cref="Nested"/> — plus another <see cref="IEnumerator"/> it drives itself. Unity coroutine types
/// (<c>WaitForSeconds</c>, ...) and game coroutines are accepted for game side enumerators the framework
/// drives, but a mod routine must not depend on them.
/// </para>
/// </summary>
public interface ICoroutineDispatcher
{
    /// <summary>
    /// The host this dispatcher binds by default: the current scene session's host for a scene dispatcher
    /// (so a routine started on it stops when the scene is unloaded) and the process host for a global one.
    /// Pass it to <see cref="StartOn"/> to tie a routine to the same lifetime.
    /// </summary>
    ICoroutineOwner Owner { get; }

    /// <summary>
    /// Starts a routine that runs until it finishes or the dispatcher stops it. It is not tied to a host, so
    /// use <see cref="StartOn"/> with <see cref="Owner"/> when the routine must die with a scene.
    /// </summary>
    CoroutineHandle Start(Func<ICoroutineDispatcher, IEnumerator> routine);

    /// <summary>
    /// Starts a routine that lives as long as <paramref name="owner"/>: it never runs when the owner is
    /// already gone, and it is stopped before its next step once the owner goes away.
    /// </summary>
    CoroutineHandle StartOn(ICoroutineOwner owner, Func<ICoroutineDispatcher, IEnumerator> routine);

    /// <summary>Stops the routine of the given handle when it belongs to this dispatcher.</summary>
    void Stop(CoroutineHandle handle);

    /// <summary>Stops every routine started through this dispatcher.</summary>
    void StopAll();

    /// <summary>Resumes the routine on the next frame.</summary>
    CoroutineAwait NextFrame { get; }

    /// <summary>Resumes the routine once <paramref name="seconds"/> of scaled game time have passed.</summary>
    CoroutineAwait AfterSeconds(float seconds);

    /// <summary>Resumes the routine once <paramref name="seconds"/> of unscaled time have passed.</summary>
    CoroutineAwait AfterSecondsRealtime(float seconds);

    /// <summary>Resumes the routine after the next fixed update.</summary>
    CoroutineAwait AfterFixedUpdate { get; }

    /// <summary>Resumes the routine after the frame that is about to end.</summary>
    CoroutineAwait AtEndOfFrame { get; }

    /// <summary>Resumes the routine once <paramref name="condition"/> returns true.</summary>
    CoroutineAwait Until(Func<bool> condition);

    /// <summary>Resumes the routine as long as <paramref name="condition"/> returns true.</summary>
    CoroutineAwait While(Func<bool> condition);

    /// <summary>Runs <paramref name="routine"/> to its end and then resumes the routine that yielded it.</summary>
    CoroutineAwait Nested(Func<ICoroutineDispatcher, IEnumerator> routine);
}
