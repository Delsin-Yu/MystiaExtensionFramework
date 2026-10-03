using System.Diagnostics.CodeAnalysis;

namespace Mystia.Assets;

// The engine side of the game data builders, named as an interface so the framework's own rules - what a
// description has to hold together, what was built once, when a table is written again - run without the
// engine. One implementation names engine types and lives in the bridge; what is decided here is what a test
// can exercise outside the game.
internal interface IGameDataAssembler
{
    /// <summary>
    /// Reports what the builder did not do, and why. The engine side owns the log, so a refusal the builder
    /// decided on is reported through here rather than by reaching for a logger the SDK does not have.
    /// </summary>
    void Trace(string message);

    /// <summary>
    /// Builds the engine objects <paramref name="spec"/> describes and keeps them under its name.
    /// </summary>
    /// <param name="spec">A description the engine free rules already accepted.</param>
    /// <param name="reason">Why the engine refused, or null on success.</param>
    bool TryAssembleDialog(DialogSpec spec, out string? reason);

    /// <summary>Writes the built package into the day scene's dialog table under <paramref name="name"/>.</summary>
    /// <param name="name">The package's name, the key the game's table uses.</param>
    /// <param name="reason">Why the game's table could not be written, or null on success.</param>
    bool TryPublishDialog(string name, out string? reason);

    /// <summary>Builds the engine object <paramref name="spec"/> describes and keeps it under its label.</summary>
    /// <param name="spec">A description the engine free rules already accepted.</param>
    /// <param name="reason">Why the engine refused, or null on success.</param>
    bool TryAssembleMissionNode(MissionNodeSpec spec, out string? reason);

    /// <summary>Builds the engine object <paramref name="spec"/> describes and keeps it under its label.</summary>
    /// <param name="spec">A description the engine free rules already accepted.</param>
    /// <param name="reason">Why the engine refused, or null on success.</param>
    bool TryAssembleEventNode(EventNodeSpec spec, out string? reason);

    /// <summary>
    /// Writes the built node into the scheduler's node table under <paramref name="label"/>, and its name and
    /// description into the mission language table (the stock event nodes have no language entry, so an event
    /// without a name writes nothing there).
    /// </summary>
    /// <param name="label">The node's label, the key the game's table uses.</param>
    /// <param name="reason">Why the game's tables could not be written, or null on success.</param>
    bool TryPublishNode(string label, out string? reason);
}

/// <summary>
/// The builder reached through <see cref="GameDataBuilders"/>. It decides everything that does not need the
/// engine (<see cref="GameDataValidation"/>) and keeps what it built, so a mod may ask whether a package or a
/// node was built without holding the handle, and a description is built once.
/// </summary>
internal sealed class GameDataBuilder : IGameDataBuilder
{
    private readonly Func<IGameDataAssembler> assembler;

    /// <summary>The builder over one engine side: what the framework's own holder builds with.</summary>
    internal GameDataBuilder(Func<IGameDataAssembler> assembler) => this.assembler = assembler;

    /// <summary>The builder over a fixed engine side: what a test that stands the engine in for uses.</summary>
    internal GameDataBuilder(IGameDataAssembler assembler) => this.assembler = () => assembler;

    private sealed record DialogEntry(DialogHandle Handle);

    private sealed record NodeEntry(SchedulerNodeHandle Handle);

    // The tables are process wide and the builder is reached from the host's own threads as well as from mods,
    // so one lock guards what was built together. Building is main thread only, which is why holding the lock
    // across the engine call costs nothing.
    private readonly object _gate = new();
    private readonly Dictionary<string, DialogEntry> _dialogs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NodeEntry> _nodes = new(StringComparer.Ordinal);

    private IGameDataAssembler Assembler => assembler();

    public bool TryBuildDialog(DialogSpec spec, [NotNullWhen(true)] out DialogHandle? dialog)
    {
        dialog = null;
        if (GameDataValidation.Dialog(spec) is { } refused)
        {
            Refuse($"the dialog '{spec?.Name}'", refused);
            return false;
        }

        var name = spec!.Name!;
        lock (_gate)
        {
            if (_dialogs.ContainsKey(name))
            {
                Refuse($"the dialog '{name}'", "a dialog package with that name was already built");
                return false;
            }

            if (!Assembler.TryAssembleDialog(spec, out var reason))
            {
                Refuse($"the dialog '{name}'", reason ?? "the engine refused the package");
                return false;
            }

            dialog = new DialogHandle(name);
            _dialogs[name] = new DialogEntry(dialog);
            Publish(dialog);
        }

        return true;
    }

    public bool TryBuildMissionNode(MissionNodeSpec spec, [NotNullWhen(true)] out MissionNodeHandle? node)
    {
        node = null;
        if (GameDataValidation.MissionNode(spec) is { } refused)
        {
            Refuse($"the mission node '{spec?.Label}'", refused);
            return false;
        }

        var label = spec!.Label!;
        lock (_gate)
        {
            if (_nodes.ContainsKey(label))
            {
                Refuse($"the node '{label}'", "a scheduler node with that label was already built");
                return false;
            }

            if (!Assembler.TryAssembleMissionNode(spec, out var reason))
            {
                Refuse($"the mission node '{label}'", reason ?? "the engine refused the node");
                return false;
            }

            node = new MissionNodeHandle(label);
            _nodes[label] = new NodeEntry(node);
            Publish(node);
        }

        return true;
    }

    public bool TryBuildEventNode(EventNodeSpec spec, [NotNullWhen(true)] out EventNodeHandle? node)
    {
        node = null;
        if (GameDataValidation.EventNode(spec) is { } refused)
        {
            Refuse($"the event node '{spec?.Label}'", refused);
            return false;
        }

        var label = spec!.Label!;
        lock (_gate)
        {
            if (_nodes.ContainsKey(label))
            {
                Refuse($"the node '{label}'", "a scheduler node with that label was already built");
                return false;
            }

            if (!Assembler.TryAssembleEventNode(spec, out var reason))
            {
                Refuse($"the event node '{label}'", reason ?? "the engine refused the node");
                return false;
            }

            node = new EventNodeHandle(label);
            _nodes[label] = new NodeEntry(node);
            Publish(node);
        }

        return true;
    }

    public bool IsDialogBuilt(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        lock (_gate)
            return _dialogs.ContainsKey(name);
    }

    public bool IsNodeBuilt(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return false;

        lock (_gate)
            return _nodes.ContainsKey(label);
    }

    /// <summary>
    /// Writes every built object into the game's own tables again. The game rebuilds those tables when its
    /// databases initialize, so the bridge calls this from the seams after each rebuild; a table that is not
    /// there (yet) leaves the object built and unpublished, which is what the handle reports.
    /// </summary>
    internal void RepublishAll()
    {
        lock (_gate)
        {
            foreach (var (name, entry) in _dialogs)
                Publish(entry.Handle);

            foreach (var (label, entry) in _nodes)
                Publish(entry.Handle);
        }
    }

    private void Publish(DialogHandle dialog)
    {
        dialog.IsPublished = false;
        if (Assembler.TryPublishDialog(dialog.Name, out var reason))
        {
            dialog.IsPublished = true;
            return;
        }

        Assembler.Trace($"GameDataBuilder: the dialog '{dialog.Name}' is built and not published yet: {reason}.");
    }

    private void Publish(SchedulerNodeHandle node)
    {
        node.IsPublished = false;
        if (Assembler.TryPublishNode(node.Label, out var reason))
        {
            node.IsPublished = true;
            return;
        }

        Assembler.Trace($"GameDataBuilder: the node '{node.Label}' is built and not published yet: {reason}.");
    }

    private void Refuse(string what, string reason) => Assembler.Trace($"GameDataBuilder: {what} was not built: {reason}.");
}

// What the builder answers before the framework installed its engine side - a plain unit test, or a process
// whose bridge never loaded. Every refusal says so, so a mod that sees one knows what is missing.
internal sealed class UnavailableAssembler : IGameDataAssembler
{
    internal static readonly UnavailableAssembler Instance = new();

    private const string Missing = "the framework's game data assembler is not installed";

    public void Trace(string message)
    {
    }

    public bool TryAssembleDialog(DialogSpec spec, out string? reason) => Refuse(out reason);

    public bool TryPublishDialog(string name, out string? reason) => Refuse(out reason);

    public bool TryAssembleMissionNode(MissionNodeSpec spec, out string? reason) => Refuse(out reason);

    public bool TryAssembleEventNode(EventNodeSpec spec, out string? reason) => Refuse(out reason);

    public bool TryPublishNode(string label, out string? reason) => Refuse(out reason);

    private static bool Refuse(out string? reason)
    {
        reason = Missing;
        return false;
    }
}
