using GamePlatform.Profiles;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace Mystia.Modding.Bridge;

// IPlatformInfo is host state rather than a notification: the game's platform profile enumerates its DLC
// keys once while the store front starts up, and the bridge stores them here for a mod to read through
// IModContext.Platform. The class exposes Shared so the host contexts (HostContext/ScopedContext) can wire
// the same instance as their Platform member.
internal sealed class PlatformInfo : IPlatformInfo
{
    internal static readonly PlatformInfo Shared = new();

    private volatile bool _resolved;

    private IReadOnlyList<string> _activeKeys = [];

    public bool KeysResolved => _resolved;

    public IReadOnlyList<string> ActiveDlcKeys => _activeKeys;

    /// <summary>Records the keys the platform resolved and flips <see cref="KeysResolved"/> to true.</summary>
    internal void Resolve(Il2CppStringArray? keys)
    {
        var resolved = new List<string>(keys?.Count ?? 0);
        if (keys is not null)
        {
            foreach (var key in keys)
            {
                if (key is not null)
                    resolved.Add(key);
            }
        }

        _activeKeys = resolved;
        _resolved = true;
    }
}

internal static class PlatformInfoSeams
{
    // The compat patch this replaces hooked the Steam profile's own GetActiveKeys; only the Steam profile
    // is patched, so a non-Steam store front leaves KeysResolved false and ActiveDlcKeys empty, which is the
    // same fallback path Core.cs takes today.
    [HarmonyPatch(typeof(SteamPlatformProfile), nameof(SteamPlatformProfile.GetActiveKeys))]
    private static class ActiveDlcKeys
    {
        private static void Postfix(Il2CppStringArray __result) => PlatformInfo.Shared.Resolve(__result);
    }
}
