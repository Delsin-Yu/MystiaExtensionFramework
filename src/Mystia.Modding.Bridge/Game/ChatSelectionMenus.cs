using DayScene.UI;
using DEYU.AdpUISystem.Managers;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mystia.Listeners;
using Mystia.Scenes;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The three values the panel asks an entry's configuration callback for, resolved once out of a mod entry: the
/// title it shows, the availability it filters on, and the action picking the entry runs.
/// </summary>
internal readonly record struct ChatMenuSelection(string Title, bool Available, Action? OnSelected);

/// <summary>
/// Turns a mod entry (<see cref="ChatMenuEntry"/>) into what the panel asks a configuration callback for. Both
/// pipelines resolve their entries here — the one that appends to a menu the game opened
/// (<see cref="ChatMenuPipeline"/>) and the one that opens a menu of the mod's own
/// (<see cref="ChatSelectionMenu"/>) — so an entry reaches the panel as the same thing either way.
/// <para>
/// Availability travels through untouched: the panel drops an unavailable entry itself, exactly as it drops an
/// unavailable entry of its own, so nothing is filtered here. The title is the entry's own label, with the mark
/// the panel parses out of a title in front of it when the entry carries one
/// (<c>DaySceneChatSelectionPannel.OpenInternal</c> reads the mark and shows the game's own sprite for it).
/// </para>
/// </summary>
internal static class ChatMenuSelections
{
    // The tags LanguageBaseHelper.TryParseGuideIconTitle reads at the start of a title; what stands between them
    // is the name of an entry of the game's own DayScene.UI.UIManager.IconMarkType, which is the vocabulary a mod
    // entry names its mark in.
    private const string MarkStart = "<GuideIcon>";
    private const string MarkEnd = "</GuideIcon>";

    internal static ChatMenuSelection Resolve(ChatMenuEntry entry) => new(Title(entry), entry.Available, entry.OnSelected);

    /// <summary>Resolves a whole list, keeping the order it was given in.</summary>
    internal static ChatMenuSelection[] Resolve(IReadOnlyList<ChatMenuEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var resolved = new ChatMenuSelection[entries.Count];
        for (var index = 0; index < entries.Count; index++)
            resolved[index] = Resolve(entries[index]);
        return resolved;
    }

    // Every mark is written down by its game name, so a build whose mark vocabulary moved fails this file's build
    // instead of drawing the wrong sprite.
    private static string Title(ChatMenuEntry entry)
    {
        var mark = entry.Icon switch
        {
            ChatMenuIcon.None => null,
            ChatMenuIcon.Event => nameof(DayScene.UI.UIManager.IconMarkType.Event),
            ChatMenuIcon.Mission => nameof(DayScene.UI.UIManager.IconMarkType.Mission),
            ChatMenuIcon.Merchant => nameof(DayScene.UI.UIManager.IconMarkType.Merchant),
            ChatMenuIcon.CanDeliver => nameof(DayScene.UI.UIManager.IconMarkType.CanDeliver),
            ChatMenuIcon.KyoukouTutorial => nameof(DayScene.UI.UIManager.IconMarkType.KyoukouTutorial),
            ChatMenuIcon.KyoukouTutorialNew => nameof(DayScene.UI.UIManager.IconMarkType.KyoukouTutorialNew),
            _ => throw new ArgumentOutOfRangeException(nameof(entry), entry.Icon, "The panel has no mark of that name."),
        };

        return mark is null ? entry.Label : MarkStart + mark + MarkEnd + entry.Label;
    }
}

/// <summary>
/// One chat selection menu a mod opened: the entries it was given, resolved once and in that order, and the game's
/// own entry that ends it.
/// <para>
/// A menu carries nothing a later menu could see. Its entries are its own copy, resolved when it is built; its
/// ending entry's key and action belong to it alone; and what sees the panel is the game. Two menus opened one
/// after another therefore never hand each other's contents to the panel, and the list a mod built a menu from can
/// be reused for the next one.
/// </para>
/// </summary>
internal sealed class ChatSelectionMenu
{
    // The panel keeps the ending entry's delegate and calls it when the player picks that entry, so the delegate
    // it was converted from has to outlive this call (ChatMenuCallbacks keeps the entry callbacks alive the same
    // way).
    private static readonly List<DaySceneChatSelectionPannel.GeneralOpenContext.EndButtonCallback> KeepAlive = [];

    private readonly ChatMenuSelection[] _entries;

    private ChatSelectionMenu(string endButtonKey, Action? onEndButton, ChatMenuSelection[] entries)
    {
        EndButtonKey = endButtonKey;
        OnEndButton = onEndButton;
        _entries = entries;
    }

    /// <summary>The entries of this menu, in the order it was given them.</summary>
    internal IReadOnlyList<ChatMenuSelection> Entries => _entries;

    /// <summary>The language key the panel labels this menu's ending entry with.</summary>
    internal string EndButtonKey { get; }

    /// <summary>What the ending entry runs after the panel's own close path, or null.</summary>
    internal Action? OnEndButton { get; }

    /// <summary>Builds one menu out of the entries and the ending entry the mod named.</summary>
    internal static ChatSelectionMenu Create(IReadOnlyList<ChatMenuEntry> entries, string endButtonKey, Action? onEndButton)
    {
        ArgumentException.ThrowIfNullOrEmpty(endButtonKey);
        return new ChatSelectionMenu(endButtonKey, onEndButton, ChatMenuSelections.Resolve(entries));
    }

    /// <summary>
    /// The ending entry was picked. The panel hands that entry its own close action, so the panel's own close path
    /// runs first and what the menu was given runs after it — which is where the "back" entry of a submenu reopens
    /// the menu above it.
    /// </summary>
    internal void EndSelected(Action closePanel)
    {
        ArgumentNullException.ThrowIfNull(closePanel);

        closePanel();
        OnEndButton?.Invoke();
    }

    /// <summary>
    /// Hands the menu to the panel: each entry becomes one of the game's configuration callbacks and the ending
    /// entry travels as the panel's own.
    /// </summary>
    /// <returns>
    /// True when the panel was asked to open. A menu of no entries opens nothing — the panel appends its own
    /// ending entry to whatever it is given, so an empty list would show a menu of one button that only closes it.
    /// </returns>
    internal bool Open()
    {
        if (_entries.Length == 0)
            return false;

        var manager = DayScene.UI.UIManager.Instance
            ?? throw new InvalidOperationException("The day scene is not open.");

        var callbacks = new Il2CppReferenceArray<DaySceneChatSelectionPannel.GetSelectionConfigurationCallback>(
            _entries.Length
        );
        for (var index = 0; index < _entries.Length; index++)
            callbacks[index] = Entry(_entries[index]);

        var endButton = DelegateSupport.ConvertDelegate<DaySceneChatSelectionPannel.GeneralOpenContext.EndButtonCallback>(
            new Action<Il2CppSystem.Action>(close => EndSelected(close.Invoke))
        ) ?? throw new InvalidOperationException("The ending entry's action could not be handed to the game.");
        KeepAlive.Add(endButton);

        // The append pipeline patches this very call, and it is told to leave a mod's own menu alone (it would
        // otherwise add the entries of IChatMenuProvider to a menu that already carries the mod's entries).
        ModChatMenuOpenings.Run(
            () => manager.OpenAfterChatMenu(
                callbacks,
                EndButtonKey,
                endButton,
                null,
                -1,
                AdpUIPanelManager.PanelVisualMode.HideVisual
            )
        );
        return true;
    }

    // One game callback per entry, built the same way an entry of an appended menu is: the label and the
    // availability are values resolved up front, and the action becomes the game's delegate only when the player
    // picks the entry.
    private static DaySceneChatSelectionPannel.GetSelectionConfigurationCallback Entry(ChatMenuSelection selection) =>
        ChatMenuCallbacks.Create(
            (
                DaySceneChatSelectionPannel.BaseInteractData? interactData,
                out string title,
                out bool availability,
                out Il2CppSystem.Action? onInteract
            ) =>
            {
                title = selection.Title;
                availability = selection.Available;
                onInteract = selection.OnSelected is null
                    ? null
                    : DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(selection.OnSelected);
            }
        );
}

/// <summary>
/// The chat selection surface a mod reaches through <see cref="ICommonServices.ChatSelection"/>: it opens a menu
/// of mod entries on the day scene's own panel, and it closes the menu the player is in.
/// </summary>
internal sealed class ChatSelectionServices : IChatSelectionServices
{
    internal static readonly ChatSelectionServices Shared = new();

    public void Open(IReadOnlyList<ChatMenuEntry> entries, string endButtonKey, Action? onEndButton = null) =>
        ChatSelectionMenu.Create(entries, endButtonKey, onEndButton).Open();

    public void Close()
    {
        // The menu the player is in is the one on top of the game's own panel stacks. Closing it here is what the
        // panel's interact data hands an appended entry as closeChatSelectionPannelCallback, asked for without
        // that per entry data, so an entry of either pipeline can leave its menu the same way.
        var panel = Top();
        if (panel is null)
            throw new InvalidOperationException("No chat selection panel is open.");

        panel.ClosePanel();
    }

    private static DaySceneChatSelectionPannel? Top()
    {
        var stacks = AdpUIPanelManager.Instance?.m_PanelStack;
        if (stacks is not { Count: > 0 })
            return null;

        var stack = stacks.Peek();
        if (stack is not { Count: > 0 })
            return null;

        return stack.Peek()?.ControlledPanel as DaySceneChatSelectionPannel;
    }
}
