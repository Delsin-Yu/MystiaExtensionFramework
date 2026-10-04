using Mystia.Scenes;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The framework's <see cref="IClock"/>. The main thread pump publishes the engine's unscaled time once per
/// frame and every reader, on any thread, gets the last published value.
/// </summary>
internal sealed class HostClock : IClock
{
    internal static readonly HostClock Shared = new();

    // Volatile because the writer is the main thread and a reader may be any other thread.
    private static volatile float _now;

    /// <summary>Publishes the engine's unscaled time; called by the main thread pump every frame.</summary>
    internal static void Publish(float unscaledTime) => _now = unscaledTime;

    public float Now => _now;
}
