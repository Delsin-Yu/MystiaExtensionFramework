using System.Collections;
using UnityEngine;

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

// Opaque yield token. Yield it directly; how it is described is internal to the framework.
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

public interface ICoroutineDispatcher
{
    CoroutineHandle Start(Func<ICoroutineDispatcher, IEnumerator> routine);

    CoroutineHandle StartOn(Component owner, Func<ICoroutineDispatcher, IEnumerator> routine);

    void Stop(CoroutineHandle handle);

    void StopAll();

    CoroutineAwait NextFrame { get; }

    CoroutineAwait AfterSeconds(float seconds);

    CoroutineAwait AfterSecondsRealtime(float seconds);

    CoroutineAwait AfterFixedUpdate { get; }

    CoroutineAwait AtEndOfFrame { get; }

    CoroutineAwait Until(Func<bool> condition);

    CoroutineAwait While(Func<bool> condition);

    CoroutineAwait Nested(Func<ICoroutineDispatcher, IEnumerator> routine);
}
