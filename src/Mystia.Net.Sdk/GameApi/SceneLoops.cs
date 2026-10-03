using Common.UI;
using GameData.Profile;
using Mystia;
using Mystia.Assets;

using UnityEngine;

namespace Mystia.Scenes;

/// <summary>
/// The host capabilities a mod may use at any time. Every member here is valid from a global loop, from a
/// scene loop, and from a background thread (<see cref="MainThread"/> hands the work back).
///
/// Anything that only makes sense while a scene runs is not part of this contract: it lives on
/// <see cref="IPresentationServices"/>, reached through the <c>Presentation</c> member of the scene services
/// a scene loop was handed.
/// </summary>
public interface ICommonServices
{
    /// <summary>The host's main thread scheduler; callable from any thread.</summary>
    IMainThreadScheduler MainThread { get; }

    /// <summary>
    /// The process level coroutine dispatcher. Its routines are not bound to a scene session, so they keep
    /// running across a scene change.
    /// </summary>
    ICoroutineDispatcher Coroutines { get; }

    /// <summary>The platform (store front) keys the host resolved at startup.</summary>
    IPlatformInfo Platform { get; }

    IDialogCatalog Dialogs { get; }

    IGuestRecords Records { get; }

    /// <summary>
    /// Builds the assets a mod draws, plays and files — textures, sprites, audio clips, pixel buffers — out of
    /// data the mod carries. Every member creates an engine object, so every member is main thread only.
    /// </summary>
    IAssetFactory Assets { get; }

    /// <summary>
    /// Files what <see cref="Assets"/> built into the game's own asset pipeline, under a key of the mod's
    /// choosing, so that game code and mods resolve the same object through the same address.
    /// </summary>
    IAssetLocator Locator { get; }

    /// <summary>
    /// Builds and publishes the day scene maps a mod ships. A map is the one asset a mod cannot reach the
    /// pipeline with on its own - the game loads it by instantiating a template the pipeline resolves - so the
    /// builder assembles that template out of a description and writes the map into the day scene's own
    /// reference table. Building creates engine objects, so it is main thread only.
    /// </summary>
    IDayMapBuilder MapBuilder { get; }

    void LoadScene(Scene scene);

    void OpenDialog(DialogPackage dialog, Action onFinished);

    void OpenDialog(DialogPackage dialog, Action onFinished, Action<Il2CppSystem.Collections.Generic.Dictionary<int, string>>? replaceText);

    void FadeIn(Action onFinished);

    void FadeOut(Action onFinished);

    void SetInputEnabled(bool enabled);

    void SetNightTransitionEnabled(bool enabled);

    /// <summary>Display text of a food tag id.</summary>
    string FoodTagText(int tagId) => throw new NotSupportedException();

    /// <summary>Display text of an evaluation level.</summary>
    string EvaluationText(int evaluation) => throw new NotSupportedException();
}
