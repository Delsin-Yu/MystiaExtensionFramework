using Mystia.Data;

namespace Mystia.Assets;

// What a description has to hold together before the engine is touched. Every rule here is decided from values
// alone, which is what lets a description the engine would refuse cost no objects and lets the rules be pinned
// outside the game process. A reason is a sentence the host log carries: the value that did not hold together,
// never a stack trace.
internal static class GameDataValidation
{
    /// <summary>The most lines one dialog package holds.</summary>
    internal const int MaxLines = 4096;

    /// <summary>The most inline actions one dialog line holds.</summary>
    internal const int MaxActionsPerLine = 256;

    /// <summary>The most options one branch holds.</summary>
    internal const int MaxOptions = 64;

    /// <summary>The most entries one reward, condition or graph list holds.</summary>
    internal const int MaxList = 256;

    /// <summary>The reason a dialog description cannot be built, or null when it can.</summary>
    internal static string? Dialog(DialogSpec? spec)
    {
        if (spec is null)
            return "the description is missing";
        if (string.IsNullOrWhiteSpace(spec.Name))
            return "it has no name";

        var lines = spec.Lines;
        if (lines is null || lines.Count == 0)
            return "it has no lines";
        if (lines.Count > MaxLines)
            return $"it has {lines.Count} lines and a package holds at most {MaxLines}";

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var number = index + 1;
            if (line is null)
                return $"line {number} is empty";
            if (!Enum.IsDefined(line.Side))
                return $"line {number} names a side that does not exist";
            if (line.Speaker is not { } speaker)
                return $"line {number} names no speaker";
            if (!Enum.IsDefined(speaker.Kind))
                return $"line {number} names a speaker kind that does not exist";
            if (string.IsNullOrWhiteSpace(line.Text))
                return $"line {number} has no text";
            if (line.OverrideSprite is not null && line.OverrideSpriteReference is not null)
                return $"line {number} names its override portrayal twice";

            var actions = line.Actions;
            if (actions is null)
                continue;
            if (actions.Count > MaxActionsPerLine)
                return $"line {number} has {actions.Count} actions and a line holds at most {MaxActionsPerLine}";

            for (var actionIndex = 0; actionIndex < actions.Count; actionIndex++)
            {
                var action = actions[actionIndex];
                var at = $"line {number} action {actionIndex + 1}";
                if (action is null)
                    return $"{at} is empty";
                if (!Enum.IsDefined(action.Kind))
                    return $"{at} names an action that does not exist";

                if (CheckAction(action, at, lines.Count) is { } refused)
                    return refused;
            }
        }

        return null;
    }

    /// <summary>The reason a mission node description cannot be built, or null when it can.</summary>
    internal static string? MissionNode(MissionNodeSpec? spec)
    {
        if (spec is null)
            return "the description is missing";
        if (string.IsNullOrWhiteSpace(spec.Label))
            return "it has no label";
        if (CheckGraph(spec.Label!, spec.PreNodes, spec.PostMissions, spec.PostMissionsAfterPerformance, spec.PostEvents) is { } refused)
            return refused;
        if (CheckList("rewards", spec.Rewards) is { } rewards)
            return rewards;
        if (CheckList("post rewards", spec.PostRewards) is { } postRewards)
            return postRewards;
        if (CheckList("finish conditions", spec.FinishConditions) is { } conditions)
            return conditions;
        if (spec.IsTimedMission && spec.MissionTimeLimit is null)
            return "a timed mission names no time limit";
        if (CheckEvent("the finish event", spec.MissionFinishEvent) is { } finish)
            return finish;
        if (CheckEvent("the failed event", spec.MissionFailedEvent) is { } failed)
            return failed;

        return null;
    }

    /// <summary>The reason an event node description cannot be built, or null when it can.</summary>
    internal static string? EventNode(EventNodeSpec? spec)
    {
        if (spec is null)
            return "the description is missing";
        if (string.IsNullOrWhiteSpace(spec.Label))
            return "it has no label";
        if (CheckGraph(spec.Label!, spec.PreNodes, spec.PostMissions, spec.PostMissionsAfterPerformance, spec.PostEvents) is { } refused)
            return refused;
        if (CheckList("rewards", spec.Rewards) is { } rewards)
            return rewards;
        if (CheckList("post rewards", spec.PostRewards) is { } postRewards)
            return postRewards;
        if (spec.ScheduledEvent is null)
            return "it plays no event";
        if (CheckEvent("the scheduled event", spec.ScheduledEvent) is { } scheduled)
            return scheduled;
        if (spec.Trigger is null)
            return "it names no trigger";

        return null;
    }

    // One inline action: the payload has to be the one its own kind carries. An image on a Null action is as
    // much a mistake as a background action without an image, and both are decided here rather than by the
    // engine, because a payload the game never reads is a description that says one thing and does another.
    private static string? CheckAction(DialogActionSpec action, string at, int lineCount)
    {
        var paysImage = action.Kind is DialogActionKind.BG or DialogActionKind.CG;
        var hasImage = action.Sprite is not null || action.SpriteReference is not null;
        var hasSound = action.Sound is not null || action.SoundReference is not null;

        if (!paysImage && hasImage)
            return $"{at} names an image its {action.Kind} action does not use";
        if (action.Kind != DialogActionKind.Sound && hasSound)
            return $"{at} names a sound its {action.Kind} action does not use";
        if (action.Kind != DialogActionKind.PlayBGM && action.BgmPackage is not null)
            return $"{at} names a music package its {action.Kind} action does not use";
        if (action.Kind != DialogActionKind.Branch && action.Options is { Count: > 0 })
            return $"{at} names branch options its {action.Kind} action does not use";
        if (action.Kind is not (DialogActionKind.Goto or DialogActionKind.End) && action.Index is not null)
            return $"{at} names a target its {action.Kind} action does not use";
        if (action.Kind != DialogActionKind.ForegroundCleaning && action.CleanSides is { Count: > 0 })
            return $"{at} names cleaning sides its {action.Kind} action does not use";

        switch (action.Kind)
        {
            case DialogActionKind.BG:
            case DialogActionKind.CG:
                if (action.Sprite is not null && action.SpriteReference is not null)
                    return $"{at} names its image twice";
                // An action that sets nothing is how a layer is cleared, so it carries no image.
                if (!hasImage && action.ShouldSet)
                    return $"{at} has no image";
                return null;

            case DialogActionKind.Sound:
                if (action.Sound is not null && action.SoundReference is not null)
                    return $"{at} names its sound twice";
                if (!hasSound)
                    return $"{at} has no sound";
                return null;

            case DialogActionKind.PlayBGM:
                return action.BgmPackage is null ? $"{at} has no music package" : null;

            case DialogActionKind.Branch:
                return CheckOptions(action.Options, at, lineCount);

            case DialogActionKind.Goto:
                if (action.Index is not { } gotoIndex)
                    return $"{at} has no target";
                return InRange(gotoIndex, lineCount) ? null : $"{at} targets line {gotoIndex}, and the package has {lineCount}";

            case DialogActionKind.End:
                if (action.Index is { } exitCode && exitCode < 0)
                    return $"{at} leaves with the exit code {exitCode}";
                return null;

            case DialogActionKind.SwitchBranch:
                return $"{at} needs a scheduler condition, which this builder does not express yet";

            case DialogActionKind.ForegroundCleaning:
                foreach (var side in action.CleanSides ?? [])
                {
                    if (!Enum.IsDefined(side))
                        return $"{at} cleans a side that does not exist";
                }

                return null;

            default:
                return null;
        }
    }

    private static string? CheckOptions(IReadOnlyList<DialogBranchOptionData>? options, string at, int lineCount)
    {
        if (options is null || options.Count == 0)
            return $"{at} has no options";
        if (options.Count > MaxOptions)
            return $"{at} has {options.Count} options and a branch holds at most {MaxOptions}";

        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            var number = index + 1;
            if (string.IsNullOrWhiteSpace(option.Text))
                return $"{at} option {number} has no text";
            if (!InRange(option.Jump, lineCount))
                return $"{at} option {number} jumps to line {option.Jump}, and the package has {lineCount}";
            if (option.Price is { } price && price < 0)
                return $"{at} option {number} costs {price}";
        }

        return null;
    }

    // One event a node plays. The event type is the game's own number, and the one the builder cannot express is
    // the timeline: a timeline is an engine asset, not a value.
    private static string? CheckEvent(string what, SchedulerEventData? data)
    {
        if (data is not { } @event)
            return null;
        if (@event.EventType == NodeEventTypes.Timeline)
            return $"{what} is a timeline, and this builder only expresses a dialog package";
        if (@event.EventType != NodeEventTypes.None && @event.EventType != NodeEventTypes.Dialog)
            return $"{what} names an event type the game does not have ({@event.EventType})";
        if (@event.EventType == NodeEventTypes.Dialog && string.IsNullOrWhiteSpace(@event.DialogPackage))
            return $"{what} names no dialog package";
        if (@event.EventType == NodeEventTypes.None && !string.IsNullOrWhiteSpace(@event.DialogPackage))
            return $"{what} names a dialog package its type does not play";

        return null;
    }

    // The graph connections: a node is one of the labels, so naming one that is not there (or naming the node
    // itself) is a graph the scheduler cannot walk.
    private static string? CheckGraph(string label, params IReadOnlyList<string>?[] lists)
    {
        foreach (var list in lists)
        {
            if (list is null)
                continue;
            if (list.Count > MaxList)
                return $"a connection list has {list.Count} labels and holds at most {MaxList}";
            foreach (var entry in list)
            {
                if (string.IsNullOrWhiteSpace(entry))
                    return "a connection list names an empty label";
                if (string.Equals(entry, label, StringComparison.Ordinal))
                    return $"the node names itself as '{label}'";
            }
        }

        return null;
    }

    private static string? CheckList<T>(string what, IReadOnlyList<T>? list)
    {
        if (list is not { Count: > MaxList })
            return null;

        return $"it has {list.Count} {what} and a node holds at most {MaxList}";
    }

    private static bool InRange(int line, int lineCount) => line >= 1 && line <= lineCount + 1;
}
