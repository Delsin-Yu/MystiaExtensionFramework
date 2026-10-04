using Mystia.Listeners;

namespace Mystia.Scenes;

/// <summary>
/// The day scene's chat selection panel — the menu of lines the player picks from. The game builds it for every
/// interaction, and a mod reaches it in two ways: it extends one of the game's own menus with entries
/// (<c>IChatMenuProvider</c>), or it opens a menu of its own here.
/// <para>
/// A menu opened here carries exactly the entries it was given, in the order it was given them, plus the game's
/// own entry that ends the menu: the framework never appends <c>IChatMenuProvider</c> entries to it, and it never
/// appends this menu's entries to anything else.
/// </para>
/// <para>
/// The service hangs off <see cref="ICommonServices"/> rather than off a scene, because the game invokes a menu
/// entry's action outside every scene loop window — opening the next menu from an entry is what a menu a mod
/// opens is for, and a member that only existed inside the loop could not be called there. The panel itself is
/// the day scene's, so a call made while that scene is not loaded is refused.
/// </para>
/// </summary>
public interface IChatSelectionServices
{
    /// <summary>
    /// Opens the panel with <paramref name="entries"/>, in that order, and the game's own entry that ends the
    /// menu last.
    /// </summary>
    /// <param name="entries">
    /// The entries to show. An entry whose availability is false is handed to the panel as unavailable — the
    /// panel drops it there, exactly as it drops an unavailable entry of a menu of its own — so a list whose
    /// entries are all unavailable opens a menu of the ending entry alone. An empty list opens no panel at all.
    /// </param>
    /// <param name="endButtonKey">
    /// The language key of the ending entry's label; the game's own menus pass <c>KIZUNA_REQUEST_END</c>. It is a
    /// key and not a label: a key the title table does not hold shows as <c>TITLE=&lt;key&gt;</c>.
    /// </param>
    /// <param name="onEndButton">
    /// Runs after the ending entry ran the panel's own close path — the step the "back" entry of a submenu takes
    /// before it opens the menu above it. Null when picking the ending entry only ends the menu.
    /// </param>
    void Open(IReadOnlyList<ChatMenuEntry> entries, string endButtonKey, Action? onEndButton = null) =>
        throw new NotSupportedException();

    /// <summary>
    /// Closes the chat selection panel the player is in. The game's own entries close their menu this way, with
    /// the close action the panel hands them through its interact data, so a mod entry of a menu — the one the
    /// game opened as well as the one a mod opened — closes it here before it opens the next one.
    /// </summary>
    /// <exception cref="InvalidOperationException">No chat selection panel is open.</exception>
    void Close() => throw new NotSupportedException();
}
