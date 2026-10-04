namespace Mystia.Scenes;

/// <summary>
/// A monotonic clock a mod may read from any thread. It measures the seconds the process has been running,
/// unaffected by the game's time scale — the value the engine calls unscaled time.
/// <para>
/// The framework reads the engine's own clock on the main thread once per frame and publishes it, so a thread
/// that is not the main one reads the last published value instead of reaching into the engine itself.
/// </para>
/// </summary>
public interface IClock
{
    /// <summary>
    /// The seconds since the process started, unaffected by the game's time scale.
    /// <para>
    /// The <c>delta</c> a loop is handed is scaled game time: it stops while the game is paused and it runs
    /// at whatever speed the game is set to. This is real elapsed time, which is what a log timestamp, a
    /// timeout or the age of a fade is measured in. It is readable from any thread; on a background thread it
    /// is the value the main thread last published, so it lags by at most one frame.
    /// </para>
    /// </summary>
    float Now { get; }
}
