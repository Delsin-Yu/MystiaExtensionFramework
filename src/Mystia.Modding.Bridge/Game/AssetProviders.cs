using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace Mystia.Modding.Bridge;

// The providers that serve a mod's in memory assets through the game's own Addressables pipeline. They come
// from the mod side of the framework: a mod used to inject these itself, which meant every mod that wanted an
// asset resolvable by address had to carry the same three classes and the same ClassInjector calls.
//
// Each asset type needs its own concrete provider: Il2CppInterop cannot inject a closed generic type, so the
// store is a managed dictionary hanging off a non generic ResourceProviderBase subclass, and the location the
// resource is filed under names the subclass through the provider id.

/// <summary>Serves sprites a mod built and filed under an address.</summary>
internal class InMemorySpriteProvider : ResourceProviderBase
{
    internal const string Id = "Mystia.Assets.Providers.Sprite";

    private static readonly Dictionary<string, Sprite> Assets = new(StringComparer.Ordinal);

    public InMemorySpriteProvider(nint pointer) : base(pointer)
    {
    }

    public InMemorySpriteProvider()
        : base(ClassInjector.DerivedConstructorPointer<InMemorySpriteProvider>())
    {
        ClassInjector.DerivedConstructorBody(this);
        m_ProviderId = Id;
    }

    public override string ProviderId => Id;

    public override Il2CppSystem.Type GetDefaultType(IResourceLocation location) => Il2CppType.Of<Sprite>();

    public override bool CanProvide(Il2CppSystem.Type type, IResourceLocation location) =>
        location is not null && location.ProviderId == Id && Assets.ContainsKey(location.InternalId);

    public override void Provide(ProvideHandle provideHandle)
    {
        var address = provideHandle.Location.InternalId;
        if (Assets.TryGetValue(address, out var sprite))
            provideHandle.Complete<Sprite>(sprite, true, null!);
        else
            provideHandle.Complete<Sprite>(null!, false, new Il2CppSystem.Exception($"{Id}: no sprite is filed under '{address}'"));
    }

    // In memory: the mod that built the sprite owns its lifetime.
    public override void Release(IResourceLocation location, Il2CppSystem.Object asset)
    {
    }

    internal static void Add(string address, Sprite sprite) => Assets[address] = sprite;

    internal static Sprite? Get(string address) => Assets.TryGetValue(address, out var sprite) ? sprite : null;

    internal static bool Remove(string address) => Assets.Remove(address);

    internal static bool Has(string address) => Assets.ContainsKey(address);
}

/// <summary>Serves audio clips a mod built and filed under an address.</summary>
internal class InMemoryAudioClipProvider : ResourceProviderBase
{
    internal const string Id = "Mystia.Assets.Providers.AudioClip";

    private static readonly Dictionary<string, AudioClip> Assets = new(StringComparer.Ordinal);

    public InMemoryAudioClipProvider(nint pointer) : base(pointer)
    {
    }

    public InMemoryAudioClipProvider()
        : base(ClassInjector.DerivedConstructorPointer<InMemoryAudioClipProvider>())
    {
        ClassInjector.DerivedConstructorBody(this);
        m_ProviderId = Id;
    }

    public override string ProviderId => Id;

    public override Il2CppSystem.Type GetDefaultType(IResourceLocation location) => Il2CppType.Of<AudioClip>();

    public override bool CanProvide(Il2CppSystem.Type type, IResourceLocation location) =>
        location is not null && location.ProviderId == Id && Assets.ContainsKey(location.InternalId);

    public override void Provide(ProvideHandle provideHandle)
    {
        var address = provideHandle.Location.InternalId;
        if (Assets.TryGetValue(address, out var clip))
            provideHandle.Complete<AudioClip>(clip, true, null!);
        else
            provideHandle.Complete<AudioClip>(null!, false, new Il2CppSystem.Exception($"{Id}: no clip is filed under '{address}'"));
    }

    public override void Release(IResourceLocation location, Il2CppSystem.Object asset)
    {
    }

    internal static void Add(string address, AudioClip clip) => Assets[address] = clip;

    internal static AudioClip? Get(string address) => Assets.TryGetValue(address, out var clip) ? clip : null;

    internal static bool Remove(string address) => Assets.Remove(address);

    internal static bool Has(string address) => Assets.ContainsKey(address);
}

/// <summary>
/// Serves GameObjects filed under an address. The game instantiates what it loads and destroys the instance,
/// so the store holds the template and releases nothing: the framework that built the template owns it.
/// </summary>
internal class InMemoryGameObjectProvider : ResourceProviderBase
{
    internal const string Id = "Mystia.Assets.Providers.GameObject";

    private static readonly Dictionary<string, GameObject> Assets = new(StringComparer.Ordinal);

    public InMemoryGameObjectProvider(nint pointer) : base(pointer)
    {
    }

    public InMemoryGameObjectProvider()
        : base(ClassInjector.DerivedConstructorPointer<InMemoryGameObjectProvider>())
    {
        ClassInjector.DerivedConstructorBody(this);
        m_ProviderId = Id;
    }

    public override string ProviderId => Id;

    public override Il2CppSystem.Type GetDefaultType(IResourceLocation location) => Il2CppType.Of<GameObject>();

    public override bool CanProvide(Il2CppSystem.Type type, IResourceLocation location) =>
        location is not null && location.ProviderId == Id && Assets.ContainsKey(location.InternalId);

    public override void Provide(ProvideHandle provideHandle)
    {
        var address = provideHandle.Location.InternalId;
        if (Assets.TryGetValue(address, out var asset))
            provideHandle.Complete<GameObject>(asset, true, null!);
        else
            provideHandle.Complete<GameObject>(null!, false, new Il2CppSystem.Exception($"{Id}: no template is filed under '{address}'"));
    }

    public override void Release(IResourceLocation location, Il2CppSystem.Object asset)
    {
    }

    internal static void Add(string address, GameObject asset) => Assets[address] = asset;

    internal static GameObject? Get(string address) => Assets.TryGetValue(address, out var asset) ? asset : null;

    internal static bool Remove(string address) => Assets.Remove(address);

    internal static bool Has(string address) => Assets.ContainsKey(address);
}
