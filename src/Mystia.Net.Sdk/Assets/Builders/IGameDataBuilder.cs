using System.Diagnostics.CodeAnalysis;

namespace Mystia.Assets;

// The builder half of the game data objects a mod ships: a dialog package and a scheduler node (a mission or
// an event). A mod hands the framework a description written in values and gets back an opaque handle; the
// framework does everything the engine side needs (create the ScriptableObject, fill its fields, file the
// assets the description names, resolve the dialog packages a node points at) and publishes what it built into
// the game's own tables.
//
// The builder keeps what it built, so a mod may ask whether a package or a node is built without holding the
// handle, and publishing is never the mod's job: the framework writes the game's tables again after every time
// the game rebuilds them.
//
// A description the framework refuses is reported as false, never thrown. What a mod does with a refusal is its
// own decision, and a refused description never reaches the engine, so a mod that got a field wrong stays in
// control of what happens next.

/// <summary>
/// Builds the game data objects a mod ships - dialog packages and scheduler nodes - out of the values it
/// describes them with, and publishes them into the game's own tables.
/// <para>
/// Every build creates an engine object (a ScriptableObject and the structures under it), so every build is
/// main thread only: from a background thread hop with <c>ICommonServices.MainThread</c> first. Publishing is
/// part of the build (and of the framework's own republishing after the game rebuilds a table); a mod never
/// publishes anything itself.
/// </para>
/// </summary>
public interface IGameDataBuilder
{
    /// <summary>
    /// Builds the dialog package <paramref name="spec"/> describes, publishes it into the day scene's dialog
    /// table under <see cref="DialogSpec.Name"/> (when that table exists) and hands back the handle the mod
    /// carries.
    /// </summary>
    /// <param name="spec">The package to build.</param>
    /// <param name="dialog">The built package, or null when the description was refused.</param>
    bool TryBuildDialog(DialogSpec spec, [NotNullWhen(true)] out DialogHandle? dialog);

    /// <summary>
    /// Builds the mission node <paramref name="spec"/> describes and publishes it into the scheduler's node
    /// table under <see cref="MissionNodeSpec.Label"/> (when that table exists).
    /// <para>
    /// A node whose event plays a dialog resolves that dialog by name against the day scene's table when it is
    /// published, so the package it names may be built (or injected) in any order.
    /// </para>
    /// </summary>
    /// <param name="spec">The node to build.</param>
    /// <param name="node">The built node, or null when the description was refused.</param>
    bool TryBuildMissionNode(MissionNodeSpec spec, [NotNullWhen(true)] out MissionNodeHandle? node);

    /// <summary>
    /// Builds the event node <paramref name="spec"/> describes and publishes it into the scheduler's node table
    /// under <see cref="EventNodeSpec.Label"/> (when that table exists).
    /// </summary>
    /// <param name="spec">The node to build.</param>
    /// <param name="node">The built node, or null when the description was refused.</param>
    bool TryBuildEventNode(EventNodeSpec spec, [NotNullWhen(true)] out EventNodeHandle? node);

    /// <summary>
    /// Whether a dialog package with this name was built by this builder (or by any other mod on the same one).
    /// A lookup over what was built, so it answers from any thread and never builds anything itself.
    /// </summary>
    /// <param name="name">The package's name.</param>
    bool IsDialogBuilt(string name);

    /// <summary>
    /// Whether a scheduler node with this label was built by this builder. A lookup over what was built, so it
    /// answers from any thread and never builds anything itself.
    /// </summary>
    /// <param name="label">The node's label.</param>
    bool IsNodeBuilt(string label);
}

/// <summary>
/// The game data builders a mod reaches. The framework installs its own builder while the host starts up; a
/// process the framework never installed one in (a plain unit test) answers every call with a refusal.
/// <para>
/// The next slice of this surface moves it onto the host capabilities (<c>ICommonServices</c>) so that a mod
/// reaches it the way it reaches every other capability, and this entry point is then the bridge's own.
/// </para>
/// </summary>
public static class GameDataBuilders
{
    private static IGameDataAssembler? s_assembler;

    // The assembler is resolved per call, so the framework may install it at any point while the process starts
    // without a mod that got in first holding a refusal forever.
    internal static readonly GameDataBuilder Shared = new(() => s_assembler ?? UnavailableAssembler.Instance);

    /// <summary>The builder every mod reaches.</summary>
    public static IGameDataBuilder Builder => Shared;

    /// <summary>The framework's own entry point: hands the builder the engine side of the work.</summary>
    internal static void Install(IGameDataAssembler assembler) => s_assembler = assembler;

    /// <summary>
    /// Publishes everything built again. The game's own tables are rebuilt when its databases initialize, so
    /// every table the framework writes into is written again after each rebuild; the bridge calls this from the
    /// seams that follow those initializations.
    /// </summary>
    internal static void RepublishAll() => Shared.RepublishAll();
}
