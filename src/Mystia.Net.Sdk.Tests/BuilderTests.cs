using System.Reflection;
using Mystia.Assets;
using Mystia.Data;
using Mystia.Modding.Bridge;
using Xunit;

namespace Mystia.Tests;

// The game data builder surface is where a mod stops assembling the game's data objects itself: it describes a
// dialog package (its lines, speakers, inline actions, images and sounds) or a scheduler node (a mission or an
// event with its rewards, conditions and trigger) in framework values, the framework builds the engine object
// and publishes it into the game's own tables. These tests pin everything the framework decides without the
// engine - the shape of the contract, what it refuses, the build once rule, the publishing state - by standing
// in for the engine side with a plain object. Whether the engine accepts the object it is handed is the part
// only the running game can answer.
public sealed class BuilderTests
{
    private static readonly string[] BannedPrefixes = ["UnityEngine", "Il2Cpp", "Il2CppInterop"];

    private const string DialogName = "mystia.tests/dialog";
    private const string MissionLabel = "mystia.tests/mission";
    private const string EventLabel = "mystia.tests/event";

    /// <summary>
    /// The whole point of the facade: a mod only ever names framework types, so no member of the builders or of
    /// the descriptions they take may name anything from the engine or the interop, value types included.
    /// </summary>
    [Theory]
    [InlineData(typeof(IGameDataBuilder))]
    [InlineData(typeof(GameDataBuilders))]
    [InlineData(typeof(DialogSpec))]
    [InlineData(typeof(DialogLineSpec))]
    [InlineData(typeof(DialogActionSpec))]
    [InlineData(typeof(SpeakerSpec))]
    [InlineData(typeof(DialogHandle))]
    [InlineData(typeof(MissionNodeSpec))]
    [InlineData(typeof(EventNodeSpec))]
    [InlineData(typeof(SchedulerNodeHandle))]
    [InlineData(typeof(MissionNodeHandle))]
    [InlineData(typeof(EventNodeHandle))]
    public void Builder_members_never_name_the_engine(Type contract)
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

    /// <summary>
    /// The descriptions a mod fills in and the handles it holds back are framework types too, and the assets a
    /// description names are the handles the factory produces or references to assets that are filed - never an
    /// engine object a mod built itself.
    /// </summary>
    [Fact]
    public void Builder_types_are_framework_types()
    {
        foreach (var type in new[]
                 {
                     typeof(IGameDataBuilder),
                     typeof(GameDataBuilders),
                     typeof(DialogSpec),
                     typeof(DialogLineSpec),
                     typeof(DialogActionSpec),
                     typeof(SpeakerSpec),
                     typeof(DialogHandle),
                     typeof(MissionNodeSpec),
                     typeof(EventNodeSpec),
                     typeof(SchedulerNodeHandle),
                     typeof(MissionNodeHandle),
                     typeof(EventNodeHandle),
                 })
        {
            Assert.Equal("Mystia", type.Namespace?.Split('.')[0]);
            Assert.Equal("Mystia.Net.Sdk", type.Assembly.GetName().Name);
        }

        // A description is built out of the handles the factory produces and the references the locator files.
        Assert.Equal(typeof(SpriteHandle), typeof(DialogActionSpec).GetProperty(nameof(DialogActionSpec.Sprite))!.PropertyType);
        Assert.Equal(typeof(AudioClipHandle), typeof(DialogActionSpec).GetProperty(nameof(DialogActionSpec.Sound))!.PropertyType);
        Assert.Equal(typeof(AssetReference), typeof(DialogActionSpec).GetProperty(nameof(DialogActionSpec.SpriteReference))!.PropertyType);
        Assert.Equal(typeof(SpriteHandle), typeof(DialogLineSpec).GetProperty(nameof(DialogLineSpec.OverrideSprite))!.PropertyType);

        // The node descriptions reuse the data face's own value types, so the same values describe a node for
        // both paths.
        Assert.Equal(typeof(SchedulerRewardData), typeof(MissionNodeSpec).GetProperty(nameof(MissionNodeSpec.Rewards))!.PropertyType.GetGenericArguments()[0]);
        Assert.Equal(typeof(MissionFinishConditionData), typeof(MissionNodeSpec).GetProperty(nameof(MissionNodeSpec.FinishConditions))!.PropertyType.GetGenericArguments()[0]);
        Assert.Equal(typeof(SchedulerTriggerData), typeof(EventNodeSpec).GetProperty(nameof(EventNodeSpec.Trigger))!.PropertyType.GetGenericArguments()[0]);
        Assert.Equal(typeof(SchedulerEventData), typeof(EventNodeSpec).GetProperty(nameof(EventNodeSpec.ScheduledEvent))!.PropertyType.GetGenericArguments()[0]);
    }

    /// <summary>
    /// The entry point: a mod reaches the builder through the framework's own holder, and asking it anything is
    /// safe before the framework installed its engine side - a lookup answers, and a description that does not
    /// hold together is refused before the engine is reached, whether the engine side is there or not.
    /// </summary>
    [Fact]
    public void The_builder_is_reachable_from_a_mod()
    {
        var builder = GameDataBuilders.Builder;

        Assert.NotNull(builder);
        Assert.False(builder.IsDialogBuilt(DialogName));
        Assert.False(builder.IsDialogBuilt(""));
        Assert.False(builder.IsDialogBuilt(null!));
        Assert.False(builder.IsNodeBuilt(MissionLabel));
        Assert.False(builder.IsNodeBuilt(null!));

        Assert.False(builder.TryBuildDialog(null!, out var dialog));
        Assert.Null(dialog);
        Assert.False(builder.TryBuildDialog(new DialogSpec { Name = DialogName }, out dialog));
        Assert.Null(dialog);
        Assert.False(builder.TryBuildMissionNode(new MissionNodeSpec(), out _));
        Assert.False(builder.TryBuildEventNode(new EventNodeSpec(), out _));
    }

    /// <summary>
    /// Bad input is reported, never thrown, and a description the framework refuses never reaches the engine:
    /// every one of these refusals is decided before the first engine call, which is what lets the whole list
    /// run outside the game process. Each entry is the description every other entry was cut down from, so the
    /// same values that build a package are the ones being refused field by field.
    /// </summary>
    [Fact]
    public void The_builder_refuses_a_description_it_cannot_build()
    {
        var line = Dialog().Lines![0];
        var action = line.Actions![0];
        var option = new DialogBranchOptionData { Text = "yes", Jump = 1 };

        var dialogs = new List<(string What, DialogSpec? Spec)>
        {
            ("no description at all", null),
            ("a name that is only whitespace", Dialog() with { Name = "  " }),
            ("no name", Dialog() with { Name = null }),
            ("no lines", Dialog() with { Lines = null }),
            ("an empty line list", Dialog() with { Lines = [] }),
            ("more lines than a package holds", Dialog() with { Lines = Enumerable.Repeat(line, 4097).ToArray() }),
            ("an empty line", Dialog() with { Lines = [null!] }),
            ("a line without a speaker", Dialog() with { Lines = [line with { Speaker = null }] }),
            ("a line without text", Dialog() with { Lines = [line with { Text = null }] }),
            ("a line with blank text", Dialog() with { Lines = [line with { Text = "   " }] }),
            ("a side that does not exist", Dialog() with { Lines = [line with { Side = (DialogSide)9 }] }),
            ("a speaker kind that does not exist", Dialog() with { Lines = [line with { Speaker = new SpeakerSpec((SpeakerKind)9, 1, 0) }] }),
            ("an override portrayal named twice", Dialog() with { Lines = [line with { OverrideSprite = new FakeSprite(), OverrideSpriteReference = new AssetReference("a", "b") }] }),
            ("more actions than a line holds", Dialog() with { Lines = [line with { Actions = Enumerable.Repeat(action, 257).ToArray() }] }),
            ("an empty action", Dialog() with { Lines = [line with { Actions = [null!] }] }),
            ("an action that does not exist", Dialog() with { Lines = [line with { Actions = [action with { Kind = (DialogActionKind)99 }] }] }),
            ("a background action without an image", Dialog() with { Lines = [line with { Actions = [action with { Kind = DialogActionKind.BG, Sprite = null }] }] }),
            ("a background action naming its image twice", Dialog() with { Lines = [line with { Actions = [action with { Kind = DialogActionKind.BG, Sprite = new FakeSprite(), SpriteReference = new AssetReference("a", "b") }] }] }),
            ("an image on an action that does not use one", Dialog() with { Lines = [line with { Actions = [action with { Kind = DialogActionKind.Null }] }] }),
            ("a sound action without a sound", Dialog() with { Lines = [line with { Actions = [new DialogActionSpec { Kind = DialogActionKind.Sound }] }] }),
            ("a sound action naming its sound twice", Dialog() with { Lines = [line with { Actions = [new DialogActionSpec { Kind = DialogActionKind.Sound, Sound = new FakeClip(), SoundReference = new AssetReference("a", "b") }] }] }),
            ("a sound on an action that does not use one", Dialog() with { Lines = [line with { Actions = [action with { Sound = new FakeClip() }] }] }),
            ("a music action without a package", Dialog() with { Lines = [line with { Actions = [new DialogActionSpec { Kind = DialogActionKind.PlayBGM }] }] }),
            ("a music package on an action that does not use one", Dialog() with { Lines = [line with { Actions = [action with { BgmPackage = new AssetReference("a", "b") }] }] }),
            ("a branch without options", Dialog() with { Lines = [line with { Actions = [new DialogActionSpec { Kind = DialogActionKind.Branch }] }] }),
            ("a branch with an option without text", Dialog() with { Lines = [line with { Actions = [new DialogActionSpec { Kind = DialogActionKind.Branch, Options = [option with { Text = " " }] }] }] }),
            ("a branch jumping before the package", Dialog() with { Lines = [line with { Actions = [new DialogActionSpec { Kind = DialogActionKind.Branch, Options = [option with { Jump = 0 }] }] }] }),
            ("a branch jumping past the package", Dialog() with { Lines = [line with { Actions = [new DialogActionSpec { Kind = DialogActionKind.Branch, Options = [option with { Jump = 3 }] }] }] }),
            ("a branch with a negative price", Dialog() with { Lines = [line with { Actions = [new DialogActionSpec { Kind = DialogActionKind.Branch, Options = [option with { Price = -1 }] }] }] }),
            ("more options than a branch holds", Dialog() with { Lines = [line with { Actions = [new DialogActionSpec { Kind = DialogActionKind.Branch, Options = Enumerable.Repeat(option, 65).ToArray() }] }] }),
            ("options on an action that does not use them", Dialog() with { Lines = [line with { Actions = [action with { Options = [option] }] }] }),
            ("a goto without a target", Dialog() with { Lines = [line with { Actions = [new DialogActionSpec { Kind = DialogActionKind.Goto }] }] }),
            ("a goto past the package", Dialog() with { Lines = [line with { Actions = [new DialogActionSpec { Kind = DialogActionKind.Goto, Index = 4 }] }] }),
            ("a target on an action that does not use one", Dialog() with { Lines = [line with { Actions = [action with { Index = 1 }] }] }),
            ("an end with a negative exit code", Dialog() with { Lines = [line with { Actions = [new DialogActionSpec { Kind = DialogActionKind.End, Index = -1 }] }] }),
            ("a switch branch", Dialog() with { Lines = [line with { Actions = [new DialogActionSpec { Kind = DialogActionKind.SwitchBranch }] }] }),
            ("cleaning a side that does not exist", Dialog() with { Lines = [line with { Actions = [new DialogActionSpec { Kind = DialogActionKind.ForegroundCleaning, CleanSides = [(DialogSide)9] }] }] }),
            ("cleaning sides on an action that does not use them", Dialog() with { Lines = [line with { Actions = [action with { CleanSides = [DialogSide.Left] }] }] }),
        };

        var reward = new SchedulerRewardData { RewardType = 19, RewardId = "someone" };
        var condition = new MissionFinishConditionData { ConditionType = 11, Label = "someone", Amount = 3 };
        var trigger = new SchedulerTriggerData { TriggerType = 2, Time = new SchedulerDayData { Day = 3 } };
        var dialogEvent = new SchedulerEventData { EventType = NodeEventTypes.Dialog, DialogPackage = DialogName };

        var missions = new List<(string What, MissionNodeSpec? Spec)>
        {
            ("no description at all", null),
            ("a label that is only whitespace", Mission() with { Label = "  " }),
            ("no label", Mission() with { Label = null }),
            ("a node naming itself as its own pre node", Mission() with { PreNodes = [MissionLabel] }),
            ("an empty label in a connection list", Mission() with { PostEvents = [""] }),
            ("more rewards than a node holds", Mission() with { Rewards = Enumerable.Repeat(reward, 257).ToArray() }),
            ("more conditions than a node holds", Mission() with { FinishConditions = Enumerable.Repeat(condition, 257).ToArray() }),
            ("a timed mission without a time limit", Mission() with { IsTimedMission = true, MissionTimeLimit = null }),
            ("a finish event that is a timeline", Mission() with { MissionFinishEvent = dialogEvent with { EventType = NodeEventTypes.Timeline } }),
            ("a finish event without a package name", Mission() with { MissionFinishEvent = dialogEvent with { DialogPackage = null } }),
            ("a finish event whose type plays nothing but names a package", Mission() with { MissionFinishEvent = dialogEvent with { EventType = NodeEventTypes.None } }),
            ("a finish event whose type does not exist", Mission() with { MissionFinishEvent = dialogEvent with { EventType = 9 } }),
        };

        var events = new List<(string What, EventNodeSpec? Spec)>
        {
            ("no description at all", null),
            ("a label that is only whitespace", Event() with { Label = "  " }),
            ("no label", Event() with { Label = null }),
            ("a node naming itself as its own post event", Event() with { PostEvents = [EventLabel] }),
            ("no scheduled event", Event() with { ScheduledEvent = null }),
            ("a scheduled event that is a timeline", Event() with { ScheduledEvent = dialogEvent with { EventType = NodeEventTypes.Timeline } }),
            ("a scheduled event without a package name", Event() with { ScheduledEvent = dialogEvent with { DialogPackage = null } }),
            ("no trigger", Event() with { Trigger = null }),
            ("more post rewards than a node holds", Event() with { PostRewards = Enumerable.Repeat(reward, 257).ToArray() }),
        };

        var assembler = new FakeAssembler();
        var builder = new GameDataBuilder(assembler);

        foreach (var (what, spec) in dialogs)
        {
            Assert.False(builder.TryBuildDialog(spec!, out var dialog), what);
            Assert.Null(dialog);
        }

        foreach (var (what, spec) in missions)
        {
            Assert.False(builder.TryBuildMissionNode(spec!, out var node), what);
            Assert.Null(node);
        }

        foreach (var (what, spec) in events)
        {
            Assert.False(builder.TryBuildEventNode(spec!, out var node), what);
            Assert.Null(node);
        }

        // Not one of them reached the engine: a refusal costs no objects, and the game is never asked.
        Assert.Empty(assembler.Assembled);

        // Every refusal was reported with a reason, which is what a mod reads in the host log.
        Assert.Equal(dialogs.Count + missions.Count + events.Count, assembler.Traces.Count);
        Assert.All(assembler.Traces, trace => Assert.Contains("was not built", trace, StringComparison.Ordinal));

        // The descriptions every refusal was cut down from are the ones that build.
        Assert.True(builder.TryBuildDialog(Dialog(), out _));
        Assert.True(builder.TryBuildMissionNode(Mission(), out _));
        Assert.True(builder.TryBuildEventNode(Event(), out _));
    }

    /// <summary>
    /// A description is built once and published through the handle it was built into: the second build of the
    /// same name is refused, the handle reports whether the game's own table names it yet, and the framework
    /// publishes what it built again whenever the game rebuilds that table.
    /// </summary>
    [Fact]
    public void A_dialog_is_built_once_and_published()
    {
        var assembler = new FakeAssembler();
        var builder = new GameDataBuilder(assembler);

        Assert.False(builder.IsDialogBuilt(DialogName));
        Assert.True(builder.TryBuildDialog(Dialog(), out var dialog));
        Assert.NotNull(dialog);
        Assert.Equal(DialogName, dialog.Name);
        Assert.Equal(DialogName, dialog.ToString());
        Assert.True(dialog.IsPublished);
        Assert.True(builder.IsDialogBuilt(DialogName));
        Assert.Equal(new[] { DialogName }, assembler.Assembled);
        Assert.Equal(new[] { DialogName }, assembler.Published);

        // A name is built once: the same package is refused, and the engine is not asked again.
        Assert.False(builder.TryBuildDialog(Dialog(), out var again));
        Assert.Null(again);
        Assert.Equal(new[] { DialogName }, assembler.Assembled);

        // The game rebuilds the table when its database initializes, and until the framework publishes again the
        // handle says so; the republish is what the bridge runs from its own seams.
        assembler.TableReady = false;
        builder.RepublishAll();
        Assert.False(dialog.IsPublished);
        assembler.TableReady = true;
        builder.RepublishAll();
        Assert.True(dialog.IsPublished);
        Assert.Equal(2, assembler.Published.Count);
    }

    /// <summary>
    /// A scheduler node is built and published the same way, and the two kinds share one table: a mission and an
    /// event are two names in one node table, so building either is what <c>IsNodeBuilt</c> answers.
    /// </summary>
    [Fact]
    public void A_node_is_built_once_and_published()
    {
        var assembler = new FakeAssembler();
        var builder = new GameDataBuilder(assembler);

        Assert.False(builder.IsNodeBuilt(MissionLabel));
        Assert.True(builder.TryBuildMissionNode(Mission(), out var mission));
        Assert.NotNull(mission);
        Assert.Equal(MissionLabel, mission.Label);
        Assert.True(mission.IsPublished);
        Assert.True(builder.IsNodeBuilt(MissionLabel));

        Assert.True(builder.TryBuildEventNode(Event(), out var @event));
        Assert.NotNull(@event);
        Assert.True(@event.IsPublished);
        Assert.Equal(new[] { MissionLabel, EventLabel }, assembler.Assembled);

        // Both kinds live in the one table, so one label is taken by the other kind and the second build of it
        // is refused.
        Assert.False(builder.TryBuildEventNode(Event() with { Label = MissionLabel }, out var clash));
        Assert.Null(clash);
        Assert.False(builder.TryBuildMissionNode(Mission(), out var again));
        Assert.Null(again);
        Assert.Equal(new[] { MissionLabel, EventLabel }, assembler.Assembled);

        assembler.TableReady = false;
        builder.RepublishAll();
        Assert.False(mission.IsPublished);
        Assert.False(@event.IsPublished);
        Assert.True(builder.IsNodeBuilt(MissionLabel));
    }

    /// <summary>
    /// The shared builder is the one the framework's own seams publish through: installing a stand in for the
    /// engine side (what the bridge does while it loads) is what makes the holder reachable end to end, which is
    /// also how a mod's call reaches the same state.
    /// </summary>
    [Fact]
    public void The_shared_builder_is_what_the_framework_publishes()
    {
        var assembler = new FakeAssembler();
        GameDataBuilders.Install(assembler);
        try
        {
            var builder = GameDataBuilders.Builder;
            Assert.True(builder.TryBuildDialog(Dialog(), out var dialog));
            Assert.True(dialog!.IsPublished);
            Assert.True(builder.IsDialogBuilt(DialogName));

            assembler.TableReady = false;
            GameDataBuilders.RepublishAll();
            Assert.False(dialog.IsPublished);

            assembler.TableReady = true;
            GameDataBuilders.RepublishAll();
            Assert.True(dialog.IsPublished);
        }
        finally
        {
            // The framework's own engine side goes back in, so the next test sees the installed state a running
            // process has.
            GameDataBuilders.Install(UnityGameDataAssembler.Shared);
        }
    }

    /// <summary>
    /// A description the engine refuses is an object that was not built: the reason is reported, no handle is
    /// handed out, the name stays free, and the same description may be built again once the engine can take it.
    /// </summary>
    [Fact]
    public void A_description_the_engine_refuses_is_reported()
    {
        var assembler = new FakeAssembler { Accept = false, Reason = "the image of line 1 action 1 is one this framework did not build" };
        var builder = new GameDataBuilder(assembler);

        Assert.False(builder.TryBuildDialog(Dialog(), out var refused));
        Assert.Null(refused);
        Assert.False(builder.IsDialogBuilt(DialogName));
        Assert.Contains("was not built", Assert.Single(assembler.Traces), StringComparison.Ordinal);
        Assert.Contains("did not build", assembler.Traces[0], StringComparison.Ordinal);

        // The description was fine, so it did reach the engine - and the name was not consumed by the refusal.
        Assert.Equal(new[] { DialogName }, assembler.Assembled);
        assembler.Accept = true;
        Assert.True(builder.TryBuildDialog(Dialog(), out var built));
        Assert.NotNull(built);
        Assert.True(built.IsPublished);
    }

    /// <summary>
    /// A background or CG action that sets nothing is how a layer is cleared, so it carries no image and is not a
    /// description that does not hold together.
    /// </summary>
    [Fact]
    public void A_background_action_that_clears_the_layer_needs_no_image()
    {
        var assembler = new FakeAssembler();
        var builder = new GameDataBuilder(assembler);
        var clearing = Dialog() with
        {
            Lines =
            [
                new DialogLineSpec
                {
                    Speaker = new SpeakerSpec(SpeakerKind.Self, 0, 0),
                    Text = "That's enough.",
                    Actions = [new DialogActionSpec { Kind = DialogActionKind.BG, ShouldSet = false }],
                },
            ],
        };

        Assert.True(builder.TryBuildDialog(clearing, out var dialog));
        Assert.NotNull(dialog);
        Assert.Equal(new[] { DialogName }, assembler.Assembled);
    }

    /// <summary>
    /// A handle is what it names: two handles for one name compare equal and are usable as dictionary keys, the
    /// kinds never equal each other, and a handle of a name that was never built is still a name.
    /// </summary>
    [Fact]
    public void Handles_compare_by_what_they_name()
    {
        var builder = new GameDataBuilder(new FakeAssembler());
        Assert.True(builder.TryBuildDialog(Dialog(), out var dialog));
        Assert.True(builder.TryBuildMissionNode(Mission(), out var mission));
        Assert.True(builder.TryBuildEventNode(Event(), out var @event));

        var sameName = new DialogHandle(DialogName);
        Assert.Equal(dialog, sameName);
        Assert.True(dialog == sameName);
        Assert.False(dialog != sameName);
        Assert.False(dialog.Equals(new DialogHandle("mystia.tests/other")));
        Assert.True(dialog != new DialogHandle("mystia.tests/other"));
        Assert.False(dialog.Equals(null));
        Assert.Equal(dialog.GetHashCode(), sameName.GetHashCode());

        var keys = new Dictionary<DialogHandle, string> { [dialog] = "built" };
        Assert.Equal("built", keys[sameName]);

        Assert.Equal(mission, new MissionNodeHandle(MissionLabel));
        Assert.Equal(@event, new EventNodeHandle(EventLabel));
        Assert.False(mission.Equals(@event));
        Assert.True(mission != @event);
        Assert.False(mission.Equals(new MissionNodeHandle(EventLabel)));
        Assert.False(mission == null);
        Assert.True((SchedulerNodeHandle?)null == (SchedulerNodeHandle?)null);
    }

    /// <summary>
    /// The engine members the builder path names, and the two things about them the compiler cannot see: the
    /// description's speaker kind, side and action kind are the framework's own enums stored by value into the
    /// game's enums, and a node's event type is the framework's own number. Everything here is metadata
    /// reflection, so it runs with the interop and before the game is touched.
    /// </summary>
    [Fact]
    public void The_builder_path_pins_the_engine_it_names() => AssetBuilderTargets.Verify();

    // The valid descriptions the refusal list is cut down from. A package of one line with a background action
    // and a two option branch, a mission that is timed and finished by talking, and an event that plays a dialog
    // when the work ends.
    private static DialogSpec Dialog() => new()
    {
        Name = DialogName,
        Lines =
        [
            new DialogLineSpec
            {
                Speaker = new SpeakerSpec(SpeakerKind.Special, 1, 0),
                Side = DialogSide.Left,
                Text = "Welcome.",
                Actions =
                [
                    new DialogActionSpec { Kind = DialogActionKind.BG, Sprite = new FakeSprite() },
                    new DialogActionSpec
                    {
                        Kind = DialogActionKind.Branch,
                        Options = [new DialogBranchOptionData { Text = "yes", Jump = 1, Price = 0 }],
                    },
                ],
            },
        ],
    };

    private static MissionNodeSpec Mission() => new()
    {
        Label = MissionLabel,
        DebugLabel = "test mission",
        Name = "A test mission",
        Description = "Written by a test.",
        MissionType = 1,
        Sender = "someone",
        Receiver = "someone else",
        Rewards = [new SchedulerRewardData { RewardType = 19, RewardId = "someone" }],
        FinishConditions = [new MissionFinishConditionData { ConditionType = 1, Label = "someone" }],
        MissionFinishEvent = new SchedulerEventData { EventType = NodeEventTypes.Dialog, DialogPackage = DialogName },
        MissionTimeLimit = new SchedulerTriggerData { TriggerType = 2, Time = new SchedulerDayData { Day = 3 } },
        IsTimedMission = true,
        Looped = true,
        HideReceiver = true,
        PreNodes = ["mystia.tests/before"],
        PostEvents = ["mystia.tests/after"],
    };

    private static EventNodeSpec Event() => new()
    {
        Label = EventLabel,
        DebugLabel = "test event",
        ScheduledEvent = new SchedulerEventData { EventType = NodeEventTypes.Dialog, DialogPackage = DialogName },
        Trigger = new SchedulerTriggerData { TriggerType = 2, Time = new SchedulerDayData { Day = 3 } },
        Rewards = [new SchedulerRewardData { RewardType = 19, RewardId = "someone" }],
        ScheduleOnce = true,
        SaveToArchiveOnce = true,
        AutoCompleteAtDayEnd = true,
        PostMissions = ["mystia.tests/after"],
    };

    // The engine side, stood in for by a plain object: the builder's rules and its state are what the tests are
    // about, and the assembler only has to say what the engine said. Every build and every publish is recorded,
    // so a test can tell a refusal that never reached the engine from one the engine made.
    private sealed class FakeAssembler : IGameDataAssembler
    {
        internal List<string> Assembled { get; } = [];

        internal List<string> Published { get; } = [];

        internal List<string> Traces { get; } = [];

        internal bool Accept { get; set; } = true;

        internal bool TableReady { get; set; } = true;

        internal string Reason { get; set; } = "the engine refused the description";

        public void Trace(string message) => Traces.Add(message);

        public bool TryAssembleDialog(DialogSpec spec, out string? reason)
        {
            Assembled.Add(spec.Name!);
            reason = Accept ? null : Reason;
            return Accept;
        }

        public bool TryPublishDialog(string name, out string? reason)
        {
            reason = TableReady ? null : "the day scene's dialog table is not initialized";
            if (TableReady)
                Published.Add(name);
            return TableReady;
        }

        public bool TryAssembleMissionNode(MissionNodeSpec spec, out string? reason)
        {
            Assembled.Add(spec.Label!);
            reason = Accept ? null : Reason;
            return Accept;
        }

        public bool TryAssembleEventNode(EventNodeSpec spec, out string? reason)
        {
            Assembled.Add(spec.Label!);
            reason = Accept ? null : Reason;
            return Accept;
        }

        public bool TryPublishNode(string label, out string? reason)
        {
            reason = TableReady ? null : "the scheduler's node table is not initialized";
            if (TableReady)
                Published.Add(label);
            return TableReady;
        }
    }

    // Handles are built by the framework, so a test that only needs something non null stands in for one: the
    // framework's own handles cannot be built outside it.
    private sealed class FakeSprite : SpriteHandle
    {
    }

    private sealed class FakeClip : AudioClipHandle
    {
    }

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
