using Mystia.Numerics;

namespace Mystia.Assets;

// An AssetBundle a mod carries, the one container of engine prefabs the framework opens. The bundle, the
// stream it was loaded from and the prefabs inside it all stay behind the bridge: a mod asks the factory to
// open the bytes it holds, reads the names out of the list it gets back, and files one of them as an effect
// template under a key of its own. No engine type is named here, so a mod that ships a bundle of effect
// prefabs never holds a GameObject, never casts one and never touches a hide flag.
/// <summary>
/// An AssetBundle a mod opened from the bytes it carries (<see cref="IAssetFactory.TryOpenBundle"/>): the
/// prefabs it holds, named the way the bundle names them, and the one call that files one of them as an effect
/// template the presentation layer plays.
/// <para>
/// The bundle and everything in it stay loaded for the process, and the framework owns them: a prefab that was
/// filed as a template is referenced by the asset table the game and the mods resolve through, so there is no
/// point at which a mod could say the game is done with it. The framework keeps the whole bundle alive instead,
/// and marks every prefab it read as one the engine must not release, because a managed reference does not stop
/// the asset teardown a scene change runs.
/// </para>
/// <para>
/// Every member here that touches the bundle is main thread only, exactly like the rest of the asset surface,
/// and none of them throws over a name a mod got wrong: a name no prefab carries is reported as false.
/// </para>
/// </summary>
public abstract class AssetBundleHandle
{
    internal AssetBundleHandle()
    {
    }

    /// <summary>
    /// The names of the prefabs this bundle holds, in the order the bundle lists them, one entry per name —
    /// a name the bundle carries twice is kept once, and the last of them is the prefab
    /// <see cref="TryRegisterPrefab"/> files. It is a plain list of strings: reading it names no engine object
    /// and does not wait for anything, which is what lets a mod check the prefabs a resource pack declares
    /// while it loads, before the first scene exists.
    /// </summary>
    public abstract IReadOnlyList<string> PrefabNames { get; }

    /// <summary>
    /// Whether the bundle holds a prefab under this name. Names are compared the way the bundle spells them,
    /// exactly (ordinal, case sensitive).
    /// </summary>
    /// <param name="prefabName">The name to look for; an empty or null name is not one the bundle carries.</param>
    public abstract bool ContainsPrefab(string prefabName);

    /// <summary>
    /// Files the prefab under <paramref name="key"/> as an effect template, the bundle's own way to
    /// <c>IPresentationServices.TryRegisterPrefab</c>: the framework clones the prefab, hides the clone, keeps
    /// it for the process, and files it into the runtime asset table that <c>PlayVfx</c> and
    /// <c>PlayScreenOverlay</c> resolve by key. The mod holds no engine object and keeps nothing alive.
    /// <para>
    /// Filing an asset is not an act of the running scene, so unlike the presentation member this one is not
    /// bound to a scene loop's window and needs no key the scene knows: it may be called while a mod loads its
    /// data as well as from inside a scene. Nothing but the main thread and a name of this bundle is required.
    /// </para>
    /// <para>
    /// A key that is filed twice replaces what it named — the asset table reports the replacement — so a mod
    /// that files one template per key from a call it makes more than once keeps track of the keys it filed
    /// itself; this member always files.
    /// </para>
    /// </summary>
    /// <param name="key">The key the template is resolved by from now on; must not be empty.</param>
    /// <param name="prefabName">A name of <see cref="PrefabNames"/>.</param>
    /// <returns>
    /// True when the template was filed. False when the key is empty, when the bundle holds no prefab under
    /// that name (or holds no prefab object for it any more), or when the runtime asset table refused it.
    /// </returns>
    public abstract bool TryRegisterPrefab(string key, string prefabName);

    /// <summary>
    /// The world position the prefab itself sits at, which is where a mod plays it when it has no opinion about
    /// where the effect goes — the presentation layer always takes a position, and the prefab of a pack was
    /// authored at the one the effect is meant to be seen at.
    /// </summary>
    /// <param name="prefabName">A name of <see cref="PrefabNames"/>.</param>
    /// <param name="position">The prefab's own position, when the call answers true.</param>
    /// <returns>False when the bundle holds no prefab under that name.</returns>
    public abstract bool TryGetPrefabPosition(string prefabName, out Vector3 position);
}
