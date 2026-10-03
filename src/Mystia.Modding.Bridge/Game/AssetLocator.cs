using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mystia.Assets;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.Util;
using Addressables = UnityEngine.AddressableAssets.Addressables;

namespace Mystia.Modding.Bridge;

// The engine side of Mystia.Assets.IAssetLocator: it files assets a mod built into the game's own Addressables
// pipeline, so that game code which resolves an address and a mod which registered one meet on the same key.
// This is the mod side code the framework absorbed: one shared runtime location map added to Addressables, the
// per type providers of AssetProviders.cs attached to the resource manager, and one location per filed key.
//
// A human readable key cannot be the address itself. The engine's AssetReference validates its own key with
// Guid.TryParse and silently skips a load it cannot parse, so a key is hashed into a deterministic GUID: the
// same key always resolves the same address, on every run and in every mod.
internal sealed class AssetLocator : IAssetLocator
{
    internal static readonly AssetLocator Shared = new();

    private const string TableId = "Mystia.Assets.RuntimeLocator";

    // The providers are shared process wide, so one lock guards the map, the filed addresses and the routing
    // table together: a mod may register from any thread it hops to the main thread from, and a lookup must
    // never see a half filed asset.
    private readonly object _gate = new();
    private readonly Dictionary<Type, Provider> _providers = [];
    private readonly HashSet<string> _addresses = new(StringComparer.Ordinal);
    private ResourceLocationMap? _table;

    public bool TryRegisterSprite(string key, SpriteHandle sprite, [NotNullWhen(true)] out AssetReference? reference)
    {
        reference = null;
        return sprite is UnitySpriteHandle handle && Register(key, handle.Sprite, typeof(Sprite), out reference);
    }

    public bool TryRegisterAudioClip(string key, AudioClipHandle clip, [NotNullWhen(true)] out AssetReference? reference)
    {
        reference = null;
        return clip is UnityAudioClipHandle handle && Register(key, handle.Clip, typeof(AudioClip), out reference);
    }

    /// <summary>
    /// Files a GameObject the framework built. A GameObject is assembled out of engine components, so there is
    /// no engine free entry point for this and no mod reaches it: it is the seam the framework's own map path
    /// files a template through, which is why it stays internal to the bridge.
    /// </summary>
    internal bool TryRegisterGameObject(string key, GameObject gameObject, out AssetReference? reference)
    {
        reference = null;
        return Register(key, gameObject, typeof(GameObject), out reference);
    }

    public bool TryGetReference(string key, [NotNullWhen(true)] out AssetReference? reference)
    {
        reference = null;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var address = AddressOf(key);
        lock (_gate)
        {
            if (!_addresses.Contains(address))
                return false;
        }

        reference = new AssetReference(key, address);
        return true;
    }

    public bool TryResolveSprite(string key, [NotNullWhen(true)] out SpriteHandle? sprite)
    {
        sprite = null;
        if (!TryResolve(key, typeof(Sprite), out var asset))
            return false;

        sprite = new UnitySpriteHandle((Sprite)asset!);
        return true;
    }

    public bool TryResolveAudioClip(string key, [NotNullWhen(true)] out AudioClipHandle? clip)
    {
        clip = null;
        if (!TryResolve(key, typeof(AudioClip), out var asset))
            return false;

        clip = new UnityAudioClipHandle((AudioClip)asset!);
        return true;
    }

    /// <summary>
    /// The GameObject template filed under <paramref name="key"/>. The template crosses this boundary because
    /// the engine has to instantiate it; it never leaves the bridge, so it stays internal like the registration
    /// that filed it.
    /// </summary>
    internal bool TryResolveGameObject(string key, [NotNullWhen(true)] out GameObject? gameObject)
    {
        gameObject = null;
        return TryResolve(key, typeof(GameObject), out var asset) && (gameObject = asset as GameObject) is not null;
    }

    public bool IsRegistered(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var address = AddressOf(key);
        lock (_gate)
        {
            return _addresses.Contains(address);
        }
    }

    public bool Unregister(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var address = AddressOf(key);
        lock (_gate)
        {
            if (!_addresses.Remove(address))
                return false;

            foreach (var provider in _providers.Values)
                provider.Remove(address);

            // The table's own key is a boxed Il2Cpp string, so the entry is removed as the same boxed value
            // the location was added with.
            _table?.Locations.Remove((Il2CppSystem.Object)(Il2CppSystem.String)address);
            return true;
        }
    }

    // ── internals ──

    private bool Register(string key, UnityEngine.Object asset, Type type, out AssetReference? reference)
    {
        reference = null;
        if (string.IsNullOrWhiteSpace(key) || asset is null)
            return false;
        if (!Table())
            return false;
        if (!_providers.TryGetValue(type, out var provider))
        {
            GameBridgeHook.Trace($"AssetLocator: nothing serves {type.Name}, so '{key}' was not filed.");
            return false;
        }

        var address = AddressOf(key);
        lock (_gate)
        {
            var first = _addresses.Add(address);
            if (!first)
                GameBridgeHook.Trace($"AssetLocator: '{key}' ({type.Name}) was already filed; the asset it named is replaced.");

            // Defensive: an unload or a scene change must not release an asset a mod's key still resolves.
            asset.hideFlags |= HideFlags.HideAndDontSave;
            provider.Add(address, asset);
            if (first)
                AddLocation(address, provider);
        }

        reference = new AssetReference(key, address);
        return true;
    }

    private bool TryResolve(string key, Type type, out UnityEngine.Object? asset)
    {
        asset = null;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var address = AddressOf(key);
        lock (_gate)
        {
            // A lookup does not build the table: before the first registration the answer is simply a miss.
            if (!_addresses.Contains(address) || !_providers.TryGetValue(type, out var provider))
                return false;

            asset = provider.Get(address);
        }

        return asset is not null;
    }

    // The runtime location table and the providers behind it. Built once, on the first registration, because
    // the game's own Addressables has to exist before a locator can be added to it.
    private bool Table()
    {
        if (_table is not null)
            return true;

        lock (_gate)
        {
            if (_table is not null)
                return true;

            try
            {
                // The providers come first: a locator that is already registered but has no provider behind it
                // would leave every location it knows about unresolvable.
                InjectProviders();
                InstallProviders();
                var table = new ResourceLocationMap(TableId, 32);
                Addressables.AddResourceLocator(table.Cast<IResourceLocator>(), null!, null!);
                _table = table;
                return true;
            }
            catch (Exception error)
            {
                GameBridgeHook.Trace($"AssetLocator: the runtime location table could not be built: {error.GetBaseException().Message}");
                return false;
            }
        }
    }

    // Il2CppInterop cannot inject a closed generic type, so every provider is its own concrete class and has
    // to be registered with the runtime before one can be fabricated.
    private static void InjectProviders()
    {
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<InMemorySpriteProvider>())
            ClassInjector.RegisterTypeInIl2Cpp<InMemorySpriteProvider>();
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<InMemoryAudioClipProvider>())
            ClassInjector.RegisterTypeInIl2Cpp<InMemoryAudioClipProvider>();
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<InMemoryGameObjectProvider>())
            ClassInjector.RegisterTypeInIl2Cpp<InMemoryGameObjectProvider>();
    }

    private void InstallProviders()
    {
        Install(
            new InMemorySpriteProvider(),
            InMemorySpriteProvider.Id,
            InMemorySpriteProvider.Add,
            InMemorySpriteProvider.Get,
            InMemorySpriteProvider.Remove,
            InMemorySpriteProvider.Has);
        Install(
            new InMemoryAudioClipProvider(),
            InMemoryAudioClipProvider.Id,
            InMemoryAudioClipProvider.Add,
            InMemoryAudioClipProvider.Get,
            InMemoryAudioClipProvider.Remove,
            InMemoryAudioClipProvider.Has);
        Install(
            new InMemoryGameObjectProvider(),
            InMemoryGameObjectProvider.Id,
            InMemoryGameObjectProvider.Add,
            InMemoryGameObjectProvider.Get,
            InMemoryGameObjectProvider.Remove,
            InMemoryGameObjectProvider.Has);
    }

    private void Install<T>(
        ResourceProviderBase provider,
        string providerId,
        Action<string, T> add,
        Func<string, T?> get,
        Func<string, bool> remove,
        Func<string, bool> has)
        where T : UnityEngine.Object
    {
        // Installing again after a provider failed to attach would route the type twice and leave two
        // providers serving one asset type.
        if (_providers.ContainsKey(typeof(T)))
            return;

        // The runtime type of the collection is ListWithEvents<IResourceProvider>, not a List<>.
        var providers = Addressables.ResourceManager.ResourceProviders.Cast<ListWithEvents<IResourceProvider>>();
        providers.Add(provider.Cast<IResourceProvider>());

        _providers[typeof(T)] = new Provider(
            providerId,
            Il2CppType.Of<T>(),
            (address, asset) => add(address, (T)asset),
            address => get(address),
            remove,
            has);
    }

    private void AddLocation(string address, Provider provider)
    {
        var location = new ResourceLocationBase(
            address,
            address,
            provider.ProviderId,
            provider.ResourceType,
            new Il2CppReferenceArray<IResourceLocation>(0));
        _table!.Add((Il2CppSystem.Object)(Il2CppSystem.String)address, location.Cast<IResourceLocation>());
    }

    /// <summary>
    /// The address a key is filed under: an MD5 of the key in the GUID shape an <c>AssetReference</c> accepts.
    /// </summary>
    private static string AddressOf(string key)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash).ToString("D");
    }

    // How one asset type is routed: the provider it belongs to and the four operations over its store.
    private sealed record Provider(
        string ProviderId,
        Il2CppSystem.Type ResourceType,
        Action<string, UnityEngine.Object> Add,
        Func<string, UnityEngine.Object?> Get,
        Func<string, bool> Remove,
        Func<string, bool> Has);
}
