using System.Reflection;
using Mystia.Listeners;
using Mystia.Modding.Bridge;
using Mystia.Scenes;
using Xunit;

namespace Mystia.Tests;

/// <summary>
/// The menu a mod opens itself: which entries reach the panel and in which order, what an unavailable entry does,
/// where the entry that ends the menu leads, how a mark becomes the title the panel parses, and that one menu's
/// contents never reach the next one.
/// <para>
/// The resolution of an entry, the menu it is put in and the mark a menu open is told about are the framework's
/// own pure code, so none of this needs the game running. Only the two calls that hand a menu to the panel are the
/// game's, and the tests stay away from them: an empty menu is the one that can be built here, because it is the
/// one that decides not to reach the panel at all.
/// </para>
/// </summary>
public sealed class ChatSelectionMenuTests
{
    private const string EndKey = "KIZUNA_REQUEST_END";

    // ---- the entries of one menu ----------------------------------------------------------------------

    [Fact]
    public void EntriesReachTheMenuInTheOrderTheyWereGiven()
    {
        var menu = Menu([Entry("first"), Entry("second"), Entry("third")]);

        Assert.Equal(new[] { "first", "second", "third" }, menu.Entries.Select(entry => entry.Title));
    }

    [Fact]
    public void AnUnavailableEntryIsKeptAndHandedToThePanelUnavailable()
    {
        // Availability is not filtered here: the panel drops an unavailable entry itself, exactly as it drops one
        // of the game's own, and an appended entry hands the panel the very same pair - the label and the flag.
        var action = () => { };
        var menu = Menu([Entry("ready"), new ChatMenuEntry("later", false, action)]);

        Assert.True(menu.Entries[0].Available);
        Assert.False(menu.Entries[1].Available);
        Assert.Equal("later", menu.Entries[1].Title);
        Assert.Same(action, menu.Entries[1].OnSelected);
    }

    [Fact]
    public void BothPipelinesResolveTheirEntriesThroughTheSameStep()
    {
        // The append pipeline and a menu a mod opens hand the panel the same thing: both turn a mod entry into a
        // ChatMenuSelection, which is the only place an entry becomes a title, an availability and an action.
        var callback = typeof(ChatMenuPipeline).GetMethod("Callback", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(callback);
        Assert.Equal(typeof(ChatMenuSelection), Assert.Single(callback!.GetParameters()).ParameterType);
    }

    [Fact]
    public void AnEntryWithoutAnActionResolvesWithoutOne()
    {
        // The panel treats a null action as an entry that does nothing when it is picked, which is what an entry
        // with no action is.
        Assert.Null(ChatMenuSelections.Resolve(new ChatMenuEntry("nothing", true, null!)).OnSelected);
    }

    // ---- the button that ends the menu -----------------------------------------------------------------

    [Fact]
    public void TheEndButtonClosesThePanelAndThenRunsWhatTheMenuWasGiven()
    {
        // The order the game's own menus use: the ending entry closes the panel and the menu's own step runs after
        // it, which is where the "back" entry of a submenu reopens the menu above.
        var trace = new List<string>();
        var menu = ChatSelectionMenu.Create([Entry("one")], EndKey, () => trace.Add("reopen"));

        menu.EndSelected(() => trace.Add("close"));

        Assert.Equal(new[] { "close", "reopen" }, trace);
    }

    [Fact]
    public void TheEndButtonOfAMenuWithoutAStepOnlyClosesThePanel()
    {
        var closed = 0;
        var menu = Menu([Entry("one")]);

        menu.EndSelected(() => closed++);

        Assert.Equal(1, closed);
        Assert.Null(menu.OnEndButton);
    }

    [Fact]
    public void AMenuNeedsAnEndButtonKeyAndSomeEntriesToBuild()
    {
        // A key is what the panel looks the ending entry's label up with, so a menu without one would show a
        // button named after nothing.
        Assert.Throws<ArgumentException>(() => ChatSelectionMenu.Create([Entry("one")], "", null));
        Assert.Throws<ArgumentNullException>(() => ChatSelectionMenu.Create(null!, EndKey, null));
    }

    [Fact]
    public void AnEmptyMenuOpensNothing()
    {
        // The panel appends its own ending entry to whatever it is given, so an empty list would show a menu of
        // one button that only closes it. The menu refuses before it reaches the panel, which is also why this
        // can be checked with no game running: were it to reach the panel, the panel manager would throw.
        var menu = ChatSelectionMenu.Create([], EndKey, null);

        Assert.False(menu.Open());
        Assert.Empty(menu.Entries);
    }

    // ---- one menu is not the next one ------------------------------------------------------------------

    [Fact]
    public void TwoMenusShareNothingBetweenThem()
    {
        var trace = new List<string>();
        var first = ChatSelectionMenu.Create([Entry("one-a")], "DLC5_LUNARCAPITALCONSOLE_CLOSE", () => trace.Add("first"));
        var second = ChatSelectionMenu.Create([Entry("two-a"), Entry("two-b")], EndKey, () => trace.Add("second"));

        Assert.Equal(new[] { "one-a" }, first.Entries.Select(entry => entry.Title));
        Assert.Equal(new[] { "two-a", "two-b" }, second.Entries.Select(entry => entry.Title));
        Assert.Equal("DLC5_LUNARCAPITALCONSOLE_CLOSE", first.EndButtonKey);
        Assert.Equal(EndKey, second.EndButtonKey);

        // The ending entry of one menu never runs the step of the other, and the other menu is untouched by it.
        first.EndSelected(() => { });
        Assert.Equal(new[] { "first" }, trace);
        Assert.Equal(2, second.Entries.Count);
    }

    [Fact]
    public void AMenuKeepsTheEntriesItWasGivenWhenTheListIsReused()
    {
        // A mod builds one list per menu and may well reuse the list: a menu is its own copy, so what the next one
        // does with that list cannot rewrite the menu already open.
        var entries = new List<ChatMenuEntry> { Entry("one") };
        var menu = ChatSelectionMenu.Create(entries, EndKey, null);

        entries.Add(Entry("two"));
        entries[0] = Entry("changed");

        Assert.Equal(new[] { "one" }, menu.Entries.Select(entry => entry.Title));
    }

    // ---- the mark of an entry --------------------------------------------------------------------------

    [Fact]
    public void TheMarkOfAnEntryBecomesTheTitleThePanelParsesOutOfIt()
    {
        // The panel parses a mark out of the title (and shows its own sprite for it), so the mark is encoded here
        // and nowhere else. An entry without a mark is handed over as it is.
        Assert.Equal(
            "<GuideIcon>Merchant</GuideIcon>Shop",
            ChatMenuSelections.Resolve(new ChatMenuEntry("Shop", true, () => { }, ChatMenuIcon.Merchant)).Title);
        Assert.Equal("Shop", ChatMenuSelections.Resolve(new ChatMenuEntry("Shop", true, () => { })).Title);
    }

    [Fact]
    public void EveryMarkTheContractOffersIsOneThePanelKnows()
    {
        foreach (var icon in Enum.GetValues<ChatMenuIcon>())
        {
            var title = ChatMenuSelections.Resolve(new ChatMenuEntry("Title", true, () => { }, icon)).Title;

            if (icon is ChatMenuIcon.None)
            {
                Assert.Equal("Title", title);
                continue;
            }

            // The panel tries Enum.TryParse on what stands between the tags, so a mark the game's own vocabulary
            // does not hold would silently show no mark at all.
            Assert.StartsWith("<GuideIcon>", title, StringComparison.Ordinal);
            var mark = title["<GuideIcon>".Length..title.IndexOf("</GuideIcon>", StringComparison.Ordinal)];
            Assert.True(Enum.TryParse<DayScene.UI.UIManager.IconMarkType>(mark, out _), mark);
        }
    }

    // ---- the menu a mod opens is the append pipeline's to leave alone ------------------------------------

    [Fact]
    public void AMenuAModOpensIsOneTheAppendPipelineLeavesAlone()
    {
        // The append pipeline patches the very call a mod's own menu goes through, so the call is marked while it
        // is made and the pipeline skips it: the entries of IChatMenuProvider belong to the game's menus, and a
        // menu that already carries the mod's entries would show them twice.
        Assert.False(ModChatMenuOpenings.Opening);

        var seen = false;
        ModChatMenuOpenings.Run(() =>
        {
            seen = ModChatMenuOpenings.Opening;
            ModChatMenuOpenings.Run(() => Assert.True(ModChatMenuOpenings.Opening));
        });

        Assert.True(seen);
        Assert.False(ModChatMenuOpenings.Opening);
    }

    // ---- the capability and its shape ------------------------------------------------------------------

    [Fact]
    public void TheChatSelectionServiceIsOneOfTheAlwaysAvailableCapabilities()
    {
        // A menu entry's action runs outside every scene loop window, so the panel hangs off ICommonServices
        // rather than off the day scene's services.
        Assert.Equal(
            typeof(IChatSelectionServices),
            typeof(ICommonServices).GetProperty(nameof(ICommonServices.ChatSelection))!.PropertyType);
        Assert.Same(ChatSelectionServices.Shared, CommonServices.Shared.ChatSelection);
    }

    [Fact]
    public void AnEmptyMenuThroughTheServiceReachesNoPanelEither()
    {
        // The one call of the surface that can be made with no game running: an empty menu is refused before the
        // panel is asked for anything.
        CommonServices.Shared.ChatSelection.Open([], EndKey);
    }

    /// <summary>
    /// The whole point of the surface: a mod may only name framework types, so no member of it may name anything
    /// from the engine or the interop - the panel, its callbacks and the interact data it hands an entry stay on
    /// the bridge's side.
    /// </summary>
    [Theory]
    [InlineData(typeof(IChatSelectionServices))]
    [InlineData(typeof(ChatMenuEntry))]
    [InlineData(typeof(ChatMenuIcon))]
    public void TheChatSelectionSurfaceNeverNamesTheEngine(Type contract)
    {
        var framework = contract.Assembly;

        foreach (var member in contract.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            foreach (var type in TypesNamedBy(member))
            {
                var name = type.Namespace ?? string.Empty;

                Assert.DoesNotContain(BannedPrefixes, prefix => name.StartsWith(prefix, StringComparison.Ordinal));
                Assert.True(
                    type.IsGenericParameter
                    || type.Assembly == framework
                    || name.StartsWith("System", StringComparison.Ordinal),
                    $"{contract.Name}.{member.Name} names {type.FullName}, which does not come from the framework.");
            }
        }
    }

    private static readonly string[] BannedPrefixes = ["UnityEngine", "Il2Cpp", "Il2CppInterop"];

    // ---- helpers -----------------------------------------------------------------------------------

    private static ChatMenuEntry Entry(string label) => new(label, true, () => { });

    private static ChatSelectionMenu Menu(ChatMenuEntry[] entries) => ChatSelectionMenu.Create(entries, EndKey, null);

    private static IEnumerable<Type> TypesNamedBy(MemberInfo member)
    {
        var named = member switch
        {
            PropertyInfo property => new[] { property.PropertyType }
                .Concat(property.GetIndexParameters().Select(parameter => parameter.ParameterType)),
            MethodInfo method => method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType),
            FieldInfo field => [field.FieldType],
            EventInfo @event when @event.EventHandlerType is { } handler => [handler],
            _ => [],
        };

        return named.SelectMany(Flatten);
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        if (type.IsArray && type.GetElementType() is { } element)
            yield return element;

        foreach (var argument in type.GetGenericArguments())
            yield return argument;
    }
}
