namespace Mystia;

public partial interface IModContext
{
    ILog Log { get; }

    IMainThreadScheduler MainThread { get; }

    IGamePaths Paths { get; }

    IIl2CppComponentHost Components { get; }

    /// <summary>Writable per-mod storage (config files and other state the mod persists).</summary>
    IModCache Cache { get; }

    /// <summary>Read-only per-mod state view.</summary>
    IModConfigSource Config { get; }

    /// <summary>
    /// Platform keys resolved at startup, readable after <see cref="IPostInitialize"/>. The default body
    /// throws until the host wiring lands, so an unwired member fails loudly instead of silently doing nothing.
    /// </summary>
    IPlatformInfo Platform => throw new NotSupportedException();
}
