using Mystia;
using Mystia.Data;

namespace Mystia.Listeners;

/// <summary>Entries of the character chat menu, in the order the game builds them.</summary>
public enum ChatOptionKind
{
    FreeChat,
    Shop,
    Mission,
    DynamicMission,
    Invite,
    RequestIngredient,
    RequestBeverage,
    Commission,
    Exit,
}

/// <summary>
/// Availability check for one chat menu entry. The flag fields describe what the game knows about the
/// character when it decides the entry.
/// </summary>
public readonly record struct ChatOptionContext(
    ChatOptionKind Option,
    string? CharacterLabel,
    CharacterKind Kind,
    int CharacterId,
    bool IsMerchant,
    bool HasMerchantData,
    int ProductCount,
    bool HasChatData,
    bool IsIgnored);

[AutoWire]
public interface IChatOptionListener
{
    /// <summary>An entry is about to be created; set <c>available</c> to false to drop it.</summary>
    void OnChatOptionAvailability(in ChatOptionContext context, ref bool available) { }
}

/// <summary>Where a chat menu is being built.</summary>
public enum ChatMenuOrigin
{
    Collab,
    CreatorBox,
    NueSlotMachine,
    Telephone,
    Console,
    Mission,
    Plugin,
    Unknown,
}

public readonly record struct ChatMenuContext(
    ChatMenuOrigin Origin,
    string? CharacterLabel,
    CharacterKind Kind,
    int CharacterId,
    bool IsMerchant);

/// <summary>
/// The mark the game draws in front of a chat menu entry — the same marks the game's own entries carry. The
/// panel parses the mark out of an entry's title, so a mod entry names it here and the framework encodes it.
/// </summary>
public enum ChatMenuIcon
{
    /// <summary>No mark; the entry shows its label alone.</summary>
    None,

    Event,

    Mission,

    Merchant,

    /// <summary>The mark of an entry that can be delivered to; the game's own shops use it for goods.</summary>
    CanDeliver,

    /// <summary>The notebook tutorial markers of the Kyouko storyline.</summary>
    KyoukouTutorial,

    /// <summary>The second, newer class of the Kyouko storyline's tutorial markers.</summary>
    KyoukouTutorialNew,
}

/// <summary>
/// One mod supplied chat menu entry: what the game shows, whether the player may pick it, and what picking it
/// runs. It is the entry shape of both menus a mod reaches — the ones the game opens, where
/// <c>IChatMenuProvider</c> appends it, and the ones a mod opens itself through
/// <c>Mystia.Scenes.IChatSelectionServices</c>.
/// <para>
/// <c>OnSelected</c> runs when the player picks the entry; it is invoked outside the menu build, so it must not
/// touch the menu it came from. Closing that menu is <c>Mystia.Scenes.IChatSelectionServices.Close</c>, which an
/// entry uses before it opens the next menu or a dialog, exactly as the game's own entries do.
/// </para>
/// </summary>
public readonly record struct ChatMenuEntry(
    string Label,
    bool Available,
    Action OnSelected,
    ChatMenuIcon Icon = ChatMenuIcon.None);

[AutoWire]
public interface IChatMenuProvider
{
    /// <summary>
    /// Append mod entries for this menu; entries are kept in the order they were added. It is only asked about the
    /// menus the game opens: a menu a mod opens through <c>Mystia.Scenes.IChatSelectionServices</c> carries exactly
    /// the entries it was given.
    /// </summary>
    void ProvideChatMenuEntries(in ChatMenuContext context, IList<ChatMenuEntry> entries) { }
}

/// <summary>Confirmations the day scene's own chat flow runs.</summary>
public enum ChatConfirmationKind
{
    /// <summary>
    /// The Yuyuko challenge's start confirmation: the game schedules the challenge's event and then starts the
    /// challenge session, both of which happen only when the player confirmed.
    /// </summary>
    YuyukoChallenge,
}

/// <summary>
/// One confirmation a character's chat is about to act on. The game's own action travels with the notification:
/// running <see cref="Confirm"/> does exactly what the game would have done, so a listener that holds the
/// confirmation keeps the action and runs it once the decision it waits for is made.
/// </summary>
public sealed class ChatConfirmationView
{
    internal ChatConfirmationView(ChatConfirmationKind kind, bool confirmed, Action confirm)
    {
        Kind = kind;
        Confirmed = confirmed;
        Confirm = confirm;
    }

    /// <summary>Which confirmation this is.</summary>
    public ChatConfirmationKind Kind { get; }

    /// <summary>
    /// The player's verdict. The game's own action does nothing when it is false, so a listener that acts on the
    /// confirmation checks it first.
    /// </summary>
    public bool Confirmed { get; }

    /// <summary>
    /// The game's own action for the confirmation, valid after the notification returns. It is the only thing
    /// that carries the confirmation out, so a listener that cancelled the invocation runs it once, whenever the
    /// decision it waits for is made.
    /// </summary>
    public Action Confirm { get; }
}

[AutoWire]
public interface IChatConfirmationListener
{
    /// <summary>
    /// A chat confirmation is about to act. Cancelling the invocation holds the game's own action and leaves it
    /// to the listener to run <see cref="ChatConfirmationView.Confirm"/> later; nothing else drives that action,
    /// so a confirmation that was cancelled and never run does not happen at all.
    /// </summary>
    void OnPreChatConfirmation(ChatConfirmationView confirmation, ref bool cancelInvocation) { }
}
