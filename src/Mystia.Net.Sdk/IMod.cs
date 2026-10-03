namespace Mystia;

/// <summary>
/// One loaded mod as the host sees it. The host hands this instance to every
/// <see cref="IInitialization"/> implementation, and it is the mod's handle on everything the host
/// offers outside a scene: its own identity, its log sink and its own storage.
/// </summary>
public interface IMod
{
    /// <summary>The mod's own id, as declared in its <c>mod.json</c>.</summary>
    string Id { get; }

    /// <summary>The mod's own version, as declared in its <c>mod.json</c>.</summary>
    string Version { get; }

    /// <summary>Absolute path of the directory the mod was loaded from.</summary>
    string Directory { get; }

    /// <summary>Log sink that tags every line with this mod's id and version.</summary>
    ILog Log { get; }

    /// <summary>Per-mod storage: the configuration the player may edit and the mod's own private state.</summary>
    IModStorage Storage { get; }
}
