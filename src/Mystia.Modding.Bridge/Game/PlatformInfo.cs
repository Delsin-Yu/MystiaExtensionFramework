using GamePlatform.Core;
using GamePlatform.MonoScripts;

namespace Mystia.Modding.Bridge;

// IPlatformInfo is host state rather than a notification: the store front resolves its DLC keys while it
// starts up, and a mod reads them to see which of its own resource packages apply. The bridge asks the
// platform the same question the game asks itself - PlatformBase.GetActiveDLCAppKey - and reads the answer.
//
// A detour was tried first, on the Steam profile's own GetActiveKeys, and it was the wrong shape for a method
// whose result the engine uses: a patch that cannot finish (an interop type initialiser that fails, an array
// this side cannot build) answers its native caller with the interop's default, and the engine's own data
// profile then inserts a null key list into its DLC labels. Reading the keys cannot do that to the engine:
// nothing is intercepted, and a read that fails leaves KeysResolved false, which is the state a mod already
// handles by falling back to loading all of its packages.
internal sealed class PlatformInfo : IPlatformInfo
{
    internal static readonly PlatformInfo Shared = new();

    private IReadOnlyList<string>? _keys;

    public bool KeysResolved => _keys is not null;

    public IReadOnlyList<string> ActiveDlcKeys => _keys ?? [];

    /// <summary>
    /// Asks the game's platform for its active keys, once, and keeps the answer. Called by the frame pump,
    /// which is the main thread: the call reaches the engine. An empty answer is an answer - a player with no
    /// DLC has no keys - so only a platform that does not exist yet leaves this unresolved.
    /// </summary>
    internal static void TryResolve()
    {
        if (Shared._keys is not null)
            return;

        var platform = GamePlatformManager.Platform;
        var keys = platform?.GetActiveDLCAppKey();
        if (keys is null)
            return;

        var resolved = new List<string>(keys.Count);
        foreach (var key in keys)
        {
            if (key is not null)
                resolved.Add(key);
        }

        Shared._keys = resolved;
    }
}
