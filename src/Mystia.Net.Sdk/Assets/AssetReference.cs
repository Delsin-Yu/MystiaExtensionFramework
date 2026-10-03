namespace Mystia.Assets;

// A registered asset, named the way the game's own pipeline names it: an addressable key. The engine's own
// reference type is not part of the contract, so what a mod holds is the key and the address the key is
// filed under; the address is what an engine AssetReference carries as its GUID, which is how a mod that
// later has to write a game asset field builds one again.
/// <summary>
/// What the game's asset pipeline knows a registered asset by: the key a mod chose and the address that key
/// is filed under. A mod hands this back to the framework wherever an asset has to be named again, and builds
/// the engine's own reference from <see cref="Address"/> where a game field expects one.
/// </summary>
public sealed class AssetReference
{
    internal AssetReference(string key, string address)
    {
        Key = key;
        Address = address;
    }

    /// <summary>The key the asset was registered under, as the mod wrote it.</summary>
    public string Key { get; }

    /// <summary>
    /// The address the asset is filed under. It is derived from the key alone and is stable across runs, so
    /// two mods — or a mod and a game table — that name the same key resolve the same asset.
    /// </summary>
    public string Address { get; }

    /// <summary>The address, the same value the engine's own reference prints.</summary>
    public override string ToString() => Address;
}
