using System.Collections;

using Mystia;
using Mystia.Scenes;

namespace Mystia.Spells;

/// <summary>
/// A mod supplied spell. The bridge creates one instance per declared spell id (or per character, when a
/// spell is shared) and runs the routine for the card that was drawn:
/// <see cref="Positive"/> for the positive card and <see cref="Negative"/> for the negative one.
///
/// A routine runs inside the work scene service scope, so the services passed to it are usable for its whole
/// length; it is driven by <paramref name="coroutines"/> and may yield <c>null</c>, an <c>ICoroutineDispatcher</c>
/// await token, a nested <see cref="IEnumerator"/> or an IL2CPP object (a game coroutine, a <c>WaitForSeconds</c>, ...).
/// Returning <c>null</c> means the card has no routine of its own; the game still runs the card's declaration,
/// portrait and buff registration paths.
/// </summary>
[AutoWire]
public interface ISpell
{
    /// <summary>The spell id this instance is registered for, as declared in the spell data.</summary>
    int SpellId { get; }

    IEnumerator? Positive(IWorkSceneServices scene, ICoroutineDispatcher coroutines, ILog log) => null;

    IEnumerator? Negative(IWorkSceneServices scene, ICoroutineDispatcher coroutines, ILog log) => null;
}
