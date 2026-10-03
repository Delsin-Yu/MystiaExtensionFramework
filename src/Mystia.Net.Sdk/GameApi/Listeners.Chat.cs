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
/// One mod supplied chat menu entry. <c>OnSelected</c> runs when the player picks the entry; it is invoked
/// outside the menu build, so it must not touch the menu it came from.
/// </summary>
public readonly record struct ChatMenuEntry(string Label, bool Available, Action OnSelected);

[AutoWire]
public interface IChatMenuProvider
{
    /// <summary>Append mod entries for this menu; entries are kept in the order they were added.</summary>
    void ProvideChatMenuEntries(in ChatMenuContext context, IList<ChatMenuEntry> entries) { }
}
