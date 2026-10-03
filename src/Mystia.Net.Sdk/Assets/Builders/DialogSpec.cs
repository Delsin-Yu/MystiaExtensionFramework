using Mystia.Data;

namespace Mystia.Assets;

// The dialog half of the game data builders. A dialog package that carries assets - a background, a line
// sound, a branch with its own option texts - cannot be expressed on the data face: DialogData carries one
// relative path per line and no line action at all, and the mod that needed more assembled the package itself
// (ScriptableObject.CreateInstance<DialogPackage>, DialogMeta[] by hand, one AssetReference per asset slot).
// This surface is that work, named in values: the mod describes the package, the framework builds it.
//
// Nothing here names an engine type. A line's text is a string, an image is the SpriteHandle the factory cut
// (or an AssetReference to an asset that is already filed), a sound is an AudioClipHandle or a reference, and
// what a mod holds back is an opaque DialogHandle. The line texts are handed to the framework with the
// description, so the game's own text replacement pass shows them - a mod never writes a replacement callback.
//
// The data face stays the other path to the same object: a DialogData entry is built while the day database
// is being filled, and a package built here is published under its name into the same table
// (DataBaseDay.allDialogPackages), so the two paths meet on the name and a mod picks the one that can express
// what it ships.

/// <summary>
/// Who says one line: the kind of speaker the game's own <c>SpeakerIdentity</c> carries, the id the kind is
/// resolved by, and which portrayal variation of that speaker is drawn.
/// </summary>
/// <param name="Kind">
/// The speaker kind. <see cref="SpeakerKind.Self"/> is the player, <see cref="SpeakerKind.Special"/> and
/// <see cref="SpeakerKind.Normal"/> are the two stock character tables, and <see cref="SpeakerKind.Unknown"/>
/// is a speaker with no name plate.
/// </param>
/// <param name="Id">The speaker's id in the table its kind names; the player ignores it.</param>
/// <param name="Portrait">Which portrayal of the speaker the line is drawn with.</param>
public readonly record struct SpeakerSpec(SpeakerKind Kind, int Id, int Portrait);

/// <summary>
/// One line's inline action, taken in the order the line lists them. Which fields an action carries is decided
/// by <see cref="Kind"/>: an image for <see cref="DialogActionKind.BG"/> and <see cref="DialogActionKind.CG"/>,
/// a clip for <see cref="DialogActionKind.Sound"/>, a package for <see cref="DialogActionKind.PlayBGM"/>,
/// options for <see cref="DialogActionKind.Branch"/>, a target for <see cref="DialogActionKind.Goto"/> and an
/// exit code for <see cref="DialogActionKind.End"/>.
/// <para>
/// An image or a clip is named either by the handle the framework built it into (<see cref="Sprite"/> /
/// <see cref="Sound"/>, filed by the builder) or by a reference to an asset that is already filed
/// (<see cref="SpriteReference"/> / <see cref="SoundReference"/>); naming the same asset both ways is refused.
/// </para>
/// </summary>
public sealed record DialogActionSpec
{
    /// <summary>The action, valued like the game's own <c>DialogPannel.ActionType</c>.</summary>
    public DialogActionKind Kind { get; init; }

    /// <summary>
    /// Whether a <see cref="DialogActionKind.BG"/> or <see cref="DialogActionKind.CG"/> action sets its image
    /// (false clears the layer), and whether <see cref="DialogActionKind.PauseResumeBGM"/> pauses (true) or
    /// resumes (false). The game's own <c>shouldSet</c>.
    /// </summary>
    public bool ShouldSet { get; init; } = true;

    /// <summary>The image the framework cut, for <see cref="DialogActionKind.BG"/> and <see cref="DialogActionKind.CG"/>.</summary>
    public SpriteHandle? Sprite { get; init; }

    /// <summary>An image that is already filed, for <see cref="DialogActionKind.BG"/> and <see cref="DialogActionKind.CG"/>.</summary>
    public AssetReference? SpriteReference { get; init; }

    /// <summary>The clip the framework built, for <see cref="DialogActionKind.Sound"/>.</summary>
    public AudioClipHandle? Sound { get; init; }

    /// <summary>A clip that is already filed, for <see cref="DialogActionKind.Sound"/>.</summary>
    public AssetReference? SoundReference { get; init; }

    /// <summary>
    /// The music package an <see cref="DialogActionKind.PlayBGM"/> action plays. A music package is a game
    /// asset, so it can only be named by reference: the framework cuts and files sprites and clips, not BGM
    /// packages.
    /// </summary>
    public AssetReference? BgmPackage { get; init; }

    /// <summary>
    /// The options of a <see cref="DialogActionKind.Branch"/>, in the order they are offered. Each option's
    /// text is shown with the framework's text replacement (like a line's), and its jump is a line number.
    /// </summary>
    public IReadOnlyList<DialogBranchOptionData>? Options { get; init; }

    /// <summary>
    /// Where a <see cref="DialogActionKind.Goto"/> jumps, and the exit code an
    /// <see cref="DialogActionKind.End"/> leaves with: a line number, 1 based, where one past the last line
    /// ends the package. Required for <see cref="DialogActionKind.Goto"/>.
    /// </summary>
    public int? Index { get; init; }

    /// <summary>
    /// The lines a <see cref="DialogActionKind.ForegroundCleaning"/> action is applied at (the game sweeps the
    /// position it names). Empty leaves the action one the game does not run.
    /// </summary>
    public IReadOnlyList<DialogSide>? CleanSides { get; init; }
}

/// <summary>
/// One line of a dialog package: who says it, what it says, and what happens around it. The line's text is the
/// only required part; the flags default to what a spoken line is (<see cref="IsSpeakInForeground"/> and
/// <see cref="UseNameInText"/> on, <see cref="IsDark"/> off).
/// </summary>
public sealed record DialogLineSpec
{
    /// <summary>The speaker; a line without one is refused, because the game draws every line as somebody.</summary>
    public SpeakerSpec? Speaker { get; init; }

    /// <summary>Which side of the panel the speaker stands on.</summary>
    public DialogSide Side { get; init; }

    /// <summary>The line's text. Required: the game's text replacement is keyed by the line number, so an empty line shows nothing.</summary>
    public string? Text { get; init; }

    /// <summary>Whether the speaker is pulled to the foreground for this line.</summary>
    public bool IsSpeakInForeground { get; init; } = true;

    /// <summary>Whether the background is dimmed for this line.</summary>
    public bool IsDark { get; init; }

    /// <summary>Whether the speaker's name is substituted into the line's text.</summary>
    public bool UseNameInText { get; init; } = true;

    /// <summary>A portrayal the framework cut, drawn instead of the speaker's own for this line.</summary>
    public SpriteHandle? OverrideSprite { get; init; }

    /// <summary>An already filed portrayal drawn instead of the speaker's own for this line.</summary>
    public AssetReference? OverrideSpriteReference { get; init; }

    /// <summary>The inline actions of this line, run in order when the line is shown.</summary>
    public IReadOnlyList<DialogActionSpec>? Actions { get; init; }
}

/// <summary>
/// Everything one dialog package is built out of: the name the game resolves it by and its lines in the order
/// they are played. A mod fills this in and hands it to <see cref="IGameDataBuilder.TryBuildDialog"/>; the
/// framework assembles the package, publishes it into the day scene's own dialog table (and publishes it again
/// every time the game rebuilds that table) and remembers the line texts.
/// <para>
/// The collections are the package's own limits: at most 4096 lines and 256 inline actions per line.
/// </para>
/// </summary>
public sealed record DialogSpec
{
    /// <summary>
    /// The package's name: the key the game's dialog table resolves it by (<c>DataBaseDay.allDialogPackages</c>)
    /// and the name the panel looks up. It is process wide, so it must be namespaced by the mod that owns the
    /// dialog; must not be empty.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>The lines, played in order; at least one is required.</summary>
    public IReadOnlyList<DialogLineSpec>? Lines { get; init; }
}

/// <summary>
/// One dialog package the builder built: what the mod holds between building the package and using it, and the
/// answer to whether the game's own table names it yet. A mod cannot build one itself - only
/// <see cref="IGameDataBuilder.TryBuildDialog"/> hands one out.
/// </summary>
public sealed class DialogHandle : IEquatable<DialogHandle>
{
    internal DialogHandle(string name) => Name = name;

    /// <summary>The package's name, as the description named it: the key the day scene's dialog table uses.</summary>
    public string Name { get; }

    /// <summary>
    /// Whether the package is in the game's dialog table right now. The table is rebuilt when the day database
    /// initializes, so a package published before that is not published any more; the framework publishes what
    /// was built again after every rebuild, which is what this reports.
    /// </summary>
    public bool IsPublished { get; internal set; }

    /// <summary>
    /// Whether the two handles name the same dialog package. A handle is what it names, so two handles for one
    /// name compare equal and a handle is usable as a dictionary key; a handle of another kind never does.
    /// </summary>
    public bool Equals(DialogHandle? other) =>
        other is not null && string.Equals(Name, other.Name, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as DialogHandle);

    /// <inheritdoc/>
    public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);

    /// <summary>The handle names the same package as <paramref name="right"/>.</summary>
    public static bool operator ==(DialogHandle? left, DialogHandle? right) => left is null ? right is null : left.Equals(right);

    /// <summary>The handle does not name the same package as <paramref name="right"/>.</summary>
    public static bool operator !=(DialogHandle? left, DialogHandle? right) => !(left == right);

    /// <summary>The package's name.</summary>
    public override string ToString() => Name;
}
