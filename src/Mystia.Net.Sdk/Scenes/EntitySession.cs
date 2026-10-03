namespace Mystia.Scenes;

// The key an entity handle is made of is "the engine object's pointer + the session it was minted in". This is
// the second half of that key: one night of business is one session, and a handle of the previous night stops
// resolving the moment the next scene loop starts.
//
// The bridge rotates the session from SceneLoopHost (one line, right where CoroutinePump.LeaveScene ends the
// scene's coroutine session), so a handle a mod kept past the night it was minted in is refused by
// TryGet instead of reaching an engine object the game already destroyed. Nothing outside the framework can
// rotate it, and a mod cannot build a handle itself.

/// <summary>
/// The session the entity handles of the running scene belong to: one night of business, or more precisely one
/// scene loop of <c>SceneLoopHost</c>. Every <see cref="GuestHandle"/>, <see cref="OrderHandle"/> and
/// <see cref="DishHandle"/> carries the session it was minted in, and
/// <see cref="GuestHandle.TryGet"/> (and its two siblings) answers false for a handle of a session that ended.
/// </summary>
internal static class EntitySession
{
    // Starts at one so the default handle (pointer zero, session zero) never resolves.
    private static int _generation = 1;

    /// <summary>The session the running scene loop belongs to.</summary>
    internal static int Generation => Volatile.Read(ref _generation);

    /// <summary>
    /// Ends the session the running scene loop belonged to: every handle minted in it stops resolving and the
    /// projections behind them are dropped. Called by the bridge when the scene loop of the previous scene has
    /// finished its Shutdown callbacks (SceneLoopHost).
    /// </summary>
    internal static void Rotate()
    {
        Interlocked.Increment(ref _generation);
        GuestDirectory.Clear();
        OrderDirectory.Clear();
        DishDirectory.Clear();
    }
}
