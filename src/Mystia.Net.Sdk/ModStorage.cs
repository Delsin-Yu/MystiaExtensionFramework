using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mystia;

/// <summary>
/// Per-mod storage. Configuration is text (the player may open and edit it); cache files are raw bytes the
/// mod owns and that never enter a save. The physical location is host defined and hidden from the mod.
/// <para>
/// Every path is relative to the mod's own area. A path that would leave it, an absolute path or a path
/// that names no file is refused: the call reports false instead of touching something outside the mod.
/// </para>
/// </summary>
public interface IModStorage
{
    /// <summary>Opens a configuration file for reading. False when it does not exist.</summary>
    bool TryOpenConfigRead(string relativePath, [NotNullWhen(true)] out TextReader? reader);

    /// <summary>Opens (creating or truncating) a configuration file for writing.</summary>
    bool TryOpenConfigWrite(string relativePath, [NotNullWhen(true)] out TextWriter? writer);

    /// <summary>Whether a cache file exists.</summary>
    bool Exists(string relativePath);

    /// <summary>Opens a cache file for reading. False when it does not exist.</summary>
    bool TryOpenRead(string relativePath, [NotNullWhen(true)] out Stream? stream);

    /// <summary>Opens (creating or truncating) a cache file for writing.</summary>
    bool TryOpenWrite(string relativePath, [NotNullWhen(true)] out Stream? stream);

    /// <summary>Deletes a cache file. True when the file is gone afterwards, including when it never existed.</summary>
    bool TryDelete(string relativePath);
}

/// <summary>
/// Reads and writes this mod's slice of the player's save. The host calls it on the main thread after the
/// game has loaded a save and right before it writes one; the mod only ever sees its own JSON, never the
/// game's save structure and never another mod's data.
/// <para>
/// One instance of this interface is registered for the whole mod, so the mod must keep the state it wants
/// to persist reachable from that instance.
/// </para>
/// </summary>
[AutoWire]
public interface IModSaveHandler
{
    /// <summary>
    /// Called after a save was loaded. <paramref name="toRead"/> is the mod's own JSON from that save, or
    /// null when the save held no data for this mod (a new game, or the data was never written).
    /// </summary>
    void OnModLoad(JsonDocument? toRead);

    /// <summary>
    /// Called right before the game writes the save. <paramref name="current"/> is the data as of the last
    /// load or save and must not be modified; the mod writes what should be stored into
    /// <paramref name="target"/>. The default implementation deep copies every property of
    /// <paramref name="current"/> into <paramref name="target"/>, which is what a mod that updates only
    /// some of its state - or nothing at all - wants.
    /// </summary>
    void OnModSave(JsonObject current, JsonObject target)
    {
        foreach (var property in current)
            target[property.Key] = property.Value?.DeepClone();
    }
}
