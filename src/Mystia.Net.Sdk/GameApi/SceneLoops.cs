using Common.UI;
using GameData.Profile;
using Mystia;

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
