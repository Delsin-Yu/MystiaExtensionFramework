namespace Mystia.Modding.Bridge;

// IPlatformInfo is host state rather than a notification: the game's platform resolves its DLC keys while the
// store front starts up. Two ways to read them were tried on this host and both fail, so the keys stay
// unresolved and a mod falls back to the load path it already has for a store front that resolves nothing.
//
//  - Detouring SteamPlatformProfile.GetActiveKeys: reading the patched result needs Il2CppStringArray, whose
//    type initialiser throws, and Il2CppInterop's native-to-managed trampoline catches that and returns the
//    default value. The engine's own data profile then receives a null array from its own platform and stops
//    loading resources - it fails inside List.InsertRange with "Value cannot be null. Parameter name:
//    collection" and raises its "cannot load game files" screen.
//  - Calling PlatformBase.GetActiveDLCAppKey through the interop: the call itself reaches the engine, but the
//    Il2CppStringArray it returns cannot be constructed either. The initialiser of Il2CppSystem.String - the
//    element type that array needs - fails to JIT with BadImageFormatException inside MonoMod's JIT hook,
//    which is installed in this process (see X64DetourProvider).
//
// The call is the right way to get the keys; it becomes usable once nothing in the process installs MonoMod's
// JIT hook. Until then this type answers what the fallback path expects.
internal sealed class PlatformInfo : IPlatformInfo
{
    internal static readonly PlatformInfo Shared = new();

    public bool KeysResolved => false;

    public IReadOnlyList<string> ActiveDlcKeys => [];
}
