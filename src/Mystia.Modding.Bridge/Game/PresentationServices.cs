using Common.UI;
using GameData.Core.Collections.NightSceneUtility;
using GameData.CoreLanguage.Collections;
using GameData.Profile;
using Mystia.Scenes;
using UnityEngine;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The presentation half of <see cref="ICommonServices"/>.
///
/// Like <c>CommonServices</c> it is handed out to every scene loop, so this class decorates that instance and
/// answers the seven presentation members on top of it.
/// Wire it where <c>CommonServices.Shared</c> is returned today:
/// <c>public ICommonServices Common => PresentationServices.Shared;</c> (splash, main, day, prep, work, staff
/// and result services, plus the global host).
/// </summary>
internal sealed class PresentationServices : ICommonServices
{
    /// <summary>The instance the scene services should hand out as <c>Common</c>.</summary>
    internal static readonly PresentationServices Shared = new(CommonServices.Shared);

    /// <summary>Asset paths already reported by the effect/audio placeholders, so a per frame call cannot
    /// flood the host log.</summary>
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    private readonly ICommonServices _inner;

    private PresentationServices(ICommonServices inner) => _inner = inner;

    public ICoroutineDispatcher Coroutines => _inner.Coroutines;

    public IDialogCatalog Dialogs => _inner.Dialogs;

    public IGuestRecords Records => _inner.Records;

    public void LoadScene(Scene scene) => _inner.LoadScene(scene);

    public void OpenDialog(DialogPackage dialog, Action onFinished) => _inner.OpenDialog(dialog, onFinished);

    public void OpenDialog(
        DialogPackage dialog,
        Action onFinished,
        Action<Il2CppSystem.Collections.Generic.Dictionary<int, string>>? replaceText) =>
        _inner.OpenDialog(dialog, onFinished, replaceText);

    public void FadeIn(Action onFinished) => _inner.FadeIn(onFinished);

    public void FadeOut(Action onFinished) => _inner.FadeOut(onFinished);

    public void SetInputEnabled(bool enabled) => _inner.SetInputEnabled(enabled);

    public void SetNightTransitionEnabled(bool enabled) => _inner.SetNightTransitionEnabled(enabled);

    public void ShakeCamera(float duration, float strength, float frequency)
    {
        ServiceScope.Require();
        // The game takes the strength first, then the duration, then the fade out, where the service takes
        // them (duration, strength, frequency).
        UniversalGameManager.SetCameraShake(strength, duration, frequency);
    }

    public IVfxHandle PlayVfx(string assetPath, Vector3 position)
    {
        ServiceScope.Require();
        Report("PlayVfx", assetPath);
        return NoopVfxHandle.Instance;
    }

    public void PlayAudio(string assetPath)
    {
        ServiceScope.Require();
        Report("PlayAudio", assetPath);
    }

    public Vector3 PlayerPosition
    {
        get
        {
            ServiceScope.Require();
            // The game's own player origin (the same call the game's spells make through SpellBase).
            return SpellBase.GetPlayerPosition();
        }
    }

    public Vector3 TablePosition(int deskCode)
    {
        ServiceScope.Require();
        // The game's own table target, i.e. what a spell aims its delivery at for that desk.
        return SpellBase.GetGuestTable(deskCode);
    }

    // The language tables are global data, not scene state, so these two read them without the scene scope.
    public string FoodTagText(int tagId) => DataBaseLanguage.GetFoodTag(tagId);

    public string EvaluationText(int evaluation) => DataBaseLanguage.GetEvalText(evaluation);

    /// <summary>
    /// Effects and audio are still placeholders. Resolving an asset path works like <c>SpriteFiles</c> does it
    /// for sprites — an absolute path stands on its own and a relative one hangs off the mod's own directory —
    /// but this service is shared by every mod and never sees the caller's <c>IModContext.Paths.ModDirectory</c>,
    /// so the call is recorded once per path in the host log and nothing is instantiated.
    /// The API keeps its shape: the effect call still hands back a handle whose <c>Stop</c> is a no-op.
    /// </summary>
    private static void Report(string member, string assetPath)
    {
        if (!Reported.Add(member + " " + assetPath))
            return;
        GameBridgeHook.Trace(
            $"{member}: '{assetPath}' is not wired yet (no per-mod asset origin on ICommonServices); the call was ignored.");
    }

    private sealed class NoopVfxHandle : IVfxHandle
    {
        internal static readonly NoopVfxHandle Instance = new();

        public void Stop()
        {
        }
    }
}
