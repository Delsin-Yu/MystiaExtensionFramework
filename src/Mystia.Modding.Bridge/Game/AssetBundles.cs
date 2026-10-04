using System.Diagnostics.CodeAnalysis;
using Il2CppInterop.Runtime;
using Mystia.Assets;
using UnityEngine;

using NumericsVector3 = Mystia.Numerics.Vector3;

namespace Mystia.Modding.Bridge;

// The engine side of the one AssetBundle entry the SDK has (IAssetFactory.TryOpenBundle). Everything a mod's
// own resource code used to do with LoadFromStream, LoadAllAssetsAsync, TryCast and hideFlags lives here now:
// the bytes are handed to the engine as a bundle, the prefabs inside it are read out and kept for the process,
// and one of them is filed as an effect template when the mod asks for it by name.
//
// Why the read is synchronous: this build's interop set has LoadFromStream and the asynchronous LoadAllAssetsAsync
// only (GetAllAssetNames, LoadAllAssets and LoadFromMemory are not there at all), so the names of a bundle's
// prefabs can be learned in exactly one way - by reading the asset list of the load request. Asking an unfinished
// request for that list stalls the caller until the load completes, which is the semantics the mod that shipped
// the read itself relied on, and it is what makes the handle able to answer "no such prefab" the moment it is
// handed out.

/// <summary>The bundle a mod opened, around the engine's own object and the prefabs read out of it.</summary>
internal sealed class UnityAssetBundleHandle : AssetBundleHandle
{
    /// <summary>The prefabs' names, in the order the bundle listed them.</summary>
    private readonly List<string> _names;

    /// <summary>The prefabs themselves, by name.</summary>
    private readonly Dictionary<string, GameObject> _prefabs;

    /// <summary>
    /// The stream the bundle was loaded from. <c>AssetBundle.LoadFromStream</c> reads through the stream for as
    /// long as the bundle is loaded, and the bundle is never unloaded (the prefabs in it are filed as templates
    /// the game itself resolves), so the stream is held for the process.
    /// </summary>
    private readonly Il2CppSystem.IO.MemoryStream? _stream;

    internal UnityAssetBundleHandle(
        List<string> names,
        Dictionary<string, GameObject> prefabs,
        Il2CppSystem.IO.MemoryStream? stream)
    {
        _names = names;
        _prefabs = prefabs;
        _stream = stream;
    }

    public override IReadOnlyList<string> PrefabNames => _names;

    public override bool ContainsPrefab(string prefabName) =>
        !string.IsNullOrEmpty(prefabName) && _prefabs.ContainsKey(prefabName);

    public override bool TryRegisterPrefab(string key, string prefabName)
    {
        // A name no prefab of this bundle carries, or a bundle that never loaded, is refused here: the engine is
        // not asked for anything before both are known.
        if (string.IsNullOrWhiteSpace(key) || prefabName is null || !Held(prefabName, out var prefab))
            return false;

        // The same work the presentation member does for an object a mod hands over by reference: the prefab is
        // cloned, the clone is hidden and filed into the runtime asset table, and the framework owns it from here
        // on. The prefab itself stays in the bundle, which is why the mod needs to hold nothing.
        return EffectPrefabs.TryRegister(key, prefab);
    }

    public override bool TryGetPrefabPosition(string prefabName, out NumericsVector3 position)
    {
        position = default;
        if (prefabName is null || !Held(prefabName, out var prefab))
            return false;

        // The prefab's own transform: an AssetBundle prefab is a scene object the bundle authored, so its
        // position is the one the effect was laid out at.
        var world = prefab.transform.position;
        position = new NumericsVector3(world.x, world.y, world.z);
        return true;
    }

    /// <summary>The prefab under a name, or false when the bundle carries none. The bundle holds its assets for
    /// the process, so a name that was read out of it always names a live object; a null reference is one a
    /// caller built by hand (a test), which the managed check alone catches.</summary>
    private bool Held(string prefabName, [NotNullWhen(true)] out GameObject? prefab) =>
        _prefabs.TryGetValue(prefabName, out prefab) && prefab is not null;
}

/// <summary>
/// Opening a bundle out of the bytes a mod carries, without ever letting the call throw or half finish: a bundle
/// this build cannot read is reported and answers false, which is the same answer empty bytes get.
/// </summary>
internal static class AssetBundles
{
    internal static bool TryOpen(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out AssetBundleHandle? bundle)
    {
        bundle = null;

        // Decided before the engine is asked for anything: empty bytes are no bundle at all.
        if (bytes.IsEmpty)
            return false;

        AssetBundle? loaded = null;
        Il2CppSystem.IO.MemoryStream? stream = null;
        try
        {
            // The interop set has no LoadFromMemory and no synchronous LoadFromFile, so the bytes reach the
            // engine through its own stream: the il2cpp side MemoryStream wraps them and the bundle reads them.
            stream = new Il2CppSystem.IO.MemoryStream(bytes.ToArray());
            loaded = AssetBundle.LoadFromStream(stream, 0);
            if (loaded is null || loaded == null)
            {
                GameBridgeHook.Trace("AssetBundles: the engine read no bundle out of the bytes it was given.");
                return false;
            }

            var names = new List<string>();
            var prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);

            // The interop set has no LoadAllAssets either, so the contents are read through the asynchronous
            // request. Asking it for its asset list before the load is done stalls the caller until it is done -
            // that is what makes this call synchronous, and why the names below are complete when it answers.
            // The Type overload is the usable one: the generic form goes through a ConvertObjects<T> this build
            // never instantiated.
            foreach (var asset in loaded.LoadAllAssetsAsync(Il2CppType.Of<GameObject>()).allAssets)
            {
                // A bundle carries more than its prefabs (textures, materials, its own manifest), and an entry
                // the engine left empty is possible as well: everything that is not a game object is skipped.
                var prefab = asset is null ? null : asset.TryCast<GameObject>();
                if (prefab is null)
                    continue;

                // A managed reference does not stop the asset teardown a scene change runs, so every prefab of a
                // bundle a mod shipped is marked as one the engine must not release: the bundle owns them, and a
                // mod's effect template is cloned out of them whenever it asks.
                prefab.hideFlags |= HideFlags.DontUnloadUnusedAsset;

                // Names are the bundle's own, one entry per name: a name the bundle carries more than once keeps
                // the last prefab under it, which is what a mod that plays that name gets.
                if (!prefabs.ContainsKey(prefab.name))
                    names.Add(prefab.name);
                prefabs[prefab.name] = prefab;
            }

            bundle = new UnityAssetBundleHandle(names, prefabs, stream);
            return true;
        }
        catch (Exception error)
        {
            // A bundle file this build cannot read (a newer format, a truncated file, a stream the engine
            // rejects) is a mod's own data, not a framework failure.
            GameBridgeHook.Trace($"AssetBundles: the bundle could not be opened: {error.GetBaseException().Message}");
            // Nothing was filed out of a bundle that was never handed out, so what was created goes again. A
            // bundle the engine refuses to release still must not hide why the open failed.
            try
            {
                loaded?.Unload(true);
            }
            catch (Exception release)
            {
                GameBridgeHook.Trace($"AssetBundles: the half read bundle could not be released: {release.GetBaseException().Message}");
            }

            bundle = null;
            return false;
        }
    }
}
