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

    /// <summary>
    /// The monotonic, unscaled clock. A loop's <c>delta</c> is scaled game time, so it is zero while the game
    /// is paused; this is the real elapsed time a mod needs for a log timestamp, a timeout or the age of a
    /// fade. It is the one member here a background thread may read.
    /// </summary>
    IClock Clock { get; }

    /// <summary>The platform (store front) keys the host resolved at startup.</summary>
    IPlatformInfo Platform { get; }

    /// <summary>
    /// The keyboard, as a mod polls it for its own global hotkeys. It reports the frame being processed, so it
    /// is main thread only, exactly like every other engine touch here.
    /// </summary>
    IInputServices Input { get; }

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
    /// Builds the game data objects a mod ships — dialog packages and scheduler (mission and event) nodes — from
    /// the descriptions in <see cref="Mystia.Assets"/>. A built object is published by the framework into the
    /// game's own tables, so a mod ships the data and the framework owns the engine objects. Main thread only.
    /// </summary>
    IGameDataBuilder DataObjects => throw new NotSupportedException();

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

    /// <summary>
    /// Whether the game's UI navigation events reach the event system. The game never writes this itself, so
    /// it stays on; a mod turns it off while it owns the screen — an open console, say — so the game's own
    /// panels stop reacting to the keys the mod's UI is reading.
    /// <para>
    /// Reading it with no event system present answers true, the engine's own default, and writing it then
    /// does nothing.
    /// </para>
    /// </summary>
    bool UiNavigationEnabled { get; set; }

    void SetNightTransitionEnabled(bool enabled);

    /// <summary>
    /// Opens <paramref name="url"/> in the player's browser. Only an absolute http or https URL is accepted:
    /// anything else — a local path, or a scheme the platform may have no handler for — is refused rather than
    /// handed to the operating system.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="url"/> is not an absolute http or https URL.</exception>
    void OpenUrl(string url);

    /// <summary>
    /// The day scene's chat selection panel, as an action a mod initiates: it opens a menu of mod entries, and it
    /// closes the menu the player is in. It is here and not on a scene because a menu entry's action runs outside
    /// every scene loop window; the panel it drives is the day scene's, which is what a call outside that scene is
    /// refused for.
    /// </summary>
    IChatSelectionServices ChatSelection => throw new NotSupportedException();

    /// <summary>Display text of a food tag id.</summary>
    string FoodTagText(int tagId) => throw new NotSupportedException();

    /// <summary>Display text of an evaluation level.</summary>
    string EvaluationText(int evaluation) => throw new NotSupportedException();
}
