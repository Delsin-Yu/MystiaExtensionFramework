using System.Diagnostics.CodeAnalysis;
using Mystia.Data;
using Mystia.Numerics;

namespace Mystia.Assets;

// The day map facade. A day scene map is the one asset a mod cannot file through the factory alone: the game
// loads a map by instantiating a template GameObject it resolves through Addressables, so a mod that ships a
// map of its own used to assemble that template itself - a root GameObject carrying a DaySceneMap, a grid of
// tilemaps, the camera bounds, the spawn points, the background music and a height map - file it into the
// runtime asset table and then write the map's address into the game's own day map reference table. This
// surface is that work, named in values: a mod describes the map, the framework builds it.
//
// Nothing here names an engine type. The art a map draws and the music it plays are the handles
// IAssetFactory hands out, the map is filed under the same kind of key IAssetLocator files everything else
// under, and the geometry is the Mystia.Numerics mirrors. What a mod holds back is an opaque DayMapHandle,
// which is also how it asks for the built map to be published.
//
// The data face is not part of this. Which maps the day scene knows, their names and descriptions, their
// spawn marker label sets and their mod mapping belong to the database injection pass (DayMapData and the
// OnInjectDayMaps hook); a builder only builds the engine objects and writes the map's address reference
// that the swap path resolves.

/// <summary>How a painted cell's sprite is turned, like the tilemap's own per cell transform.</summary>
public enum DayMapTileRotation : byte
{
    /// <summary>The sprite is drawn the way it was cut.</summary>
    None = 0,

    /// <summary>A quarter turn counter clockwise, the direction the engine's own positive rotation turns.</summary>
    Degrees90 = 1,

    /// <summary>A half turn.</summary>
    Degrees180 = 2,

    /// <summary>Three quarters of a turn counter clockwise.</summary>
    Degrees270 = 3,
}

/// <summary>
/// One sprite of a map's palette: the key the layers and the props of the same map refer to, and the sprite
/// that draws it. A tile carries a sprite the framework cut (<c>IAssetFactory.TryCreateSprite</c>), so a mod
/// never names an engine sprite; the engine tile object is built out of it behind the builder.
/// </summary>
/// <param name="Key">The key a cell or a prop names this tile by; must not be empty and must be unique in the map.</param>
/// <param name="Sprite">The sprite that draws it; a tile without one is refused.</param>
public readonly record struct DayMapTile(string Key, SpriteHandle? Sprite);

/// <summary>
/// One painted cell of a tile layer. A layer is a set of cells, so a cell is a position and the palette key
/// of the tile drawn there; the map's own cell size decides how big that is in world units.
/// </summary>
/// <param name="X">The cell's column in the grid.</param>
/// <param name="Y">The cell's row in the grid.</param>
/// <param name="Tile">The palette key of the tile drawn here.</param>
public readonly record struct DayMapCell(int X, int Y, string Tile)
{
    /// <summary>How the sprite is turned inside the cell. <see cref="DayMapTileRotation.None"/> leaves it as cut.</summary>
    public DayMapTileRotation Rotation { get; init; }

    /// <summary>
    /// The tint the sprite is drawn with, or null for the sprite's own colours. A tint is the engine's own
    /// per cell colour, so a map can shade one cell without a second sprite.
    /// </summary>
    public Color? Tint { get; init; }
}

/// <summary>
/// One tile layer of a map: a sorting layer, a sorting order and the cells painted with the map's palette.
/// The layer's own extent is its cells - the engine compresses the tilemap to what was painted - and the size
/// of one cell comes from the map's <see cref="DayMapSpec.CellSize"/>.
/// </summary>
public sealed record DayMapLayer
{
    /// <summary>The layer object's name; null leaves the game's own default.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// The Unity sorting layer the layer draws in (the game's own maps use <c>Background</c> and the layers
    /// above it). A name no sorting layer of the running game has is refused, because the engine has no
    /// sorting layer to file the renderer under.
    /// </summary>
    public string? SortingLayer { get; init; }

    /// <summary>The order inside that sorting layer; a signed 16 bit value, like the renderer's own.</summary>
    public int SortingOrder { get; init; }

    /// <summary>The painted cells; must not be null and must not name a tile the palette does not hold.</summary>
    public IReadOnlyList<DayMapCell>? Cells { get; init; }
}

/// <summary>
/// One sprite standing on the map: a prop is a single sprite with a position, a scale and a sorting layer of
/// its own, which is how a map places a tree, a sign or a lamp without painting a whole tile layer.
/// </summary>
public sealed record DayMapProp
{
    /// <summary>The object's name; null falls back to the palette key of the tile it draws.</summary>
    public string? Name { get; init; }

    /// <summary>The palette key of the sprite it draws.</summary>
    public string? Tile { get; init; }

    /// <summary>Where it stands, in world units.</summary>
    public Vector2 Position { get; init; }

    /// <summary>Its scale; both components must be positive (a scale of zero draws nothing).</summary>
    public Vector2 Scale { get; init; } = new(1f, 1f);

    /// <summary>
    /// Whether props are sorted by their y so that one behind another overlaps correctly. A prop the player
    /// can walk behind needs it; the y is then limited to the range that controller sorts in.
    /// </summary>
    public bool SortByY { get; init; } = true;

    /// <summary>The Unity sorting layer the prop draws in; must be one the running game has.</summary>
    public string? SortingLayer { get; init; }

    /// <summary>The order inside that sorting layer; a signed 16 bit value.</summary>
    public int SortingOrder { get; init; }
}

/// <summary>
/// One invisible collision box of a map: what stops the player, named for the object it becomes. A box is a
/// position and a size in world units, centered on the position.
/// </summary>
/// <param name="Name">The object's name; null leaves the game's own default.</param>
/// <param name="Position">The center of the box, in world units.</param>
/// <param name="Size">The width and the height of the box; both must be positive.</param>
public readonly record struct DayMapCollision(string? Name, Vector2 Position, Vector2 Size);

/// <summary>
/// One spawn point of a map: where the player (or a character told to use it) is put down, which way it faces
/// there, and how far its reach goes.
/// <para>
/// The engine object is named after the map's label and this label together -
/// <c>{map label}_{marker label}</c>, the same name the database pass writes into the map's spawn marker
/// label set - so a mod that injects the matching <c>DayMapData.SpawnMarkers</c> entry reaches this point.
/// </para>
/// </summary>
/// <param name="Label">The point's own name; must not be empty and must be unique in the map.</param>
/// <param name="Position">Where it stands, in world units.</param>
/// <param name="Rotation">The facing a character using this point is given.</param>
public readonly record struct DayMapSpawnMarker(string? Label, Vector2 Position, CharacterRotationKind Rotation)
{
    /// <summary>
    /// The reach this point gives the marker, or null to leave the marker's own alone. The engine's spawn
    /// marker carries a radius and the flag that makes it use a radius other than the one it was built with,
    /// which is what this sets; a radius that is not positive is refused rather than handed to the engine.
    /// </summary>
    public float? Radius { get; init; }
}

/// <summary>
/// One cell of a map's height map: the slope the player walks on when moving right through it. The engine
/// stores the height as a texture the player controller samples, so a map that has no height cells is flat.
/// </summary>
/// <param name="X">The cell's column in the grid.</param>
/// <param name="Y">The cell's row in the grid.</param>
/// <param name="Slope">
/// The vertical displacement (in cells) per cell moved right, in -1..1; positive climbs, negative descends.
/// </param>
public readonly record struct DayMapHeightCell(int X, int Y, float Slope);

/// <summary>Where the day scene camera starts, and the rectangle it stays inside while it follows the player.</summary>
public sealed record DayMapCamera
{
    /// <summary>Whether the camera follows the player. A map that fits one screen leaves it out.</summary>
    public bool Follows { get; init; } = true;

    /// <summary>
    /// The camera's default position. The z is the depth the game's own day scene camera sits at, so the
    /// default (0, 0, -10) is the one a 2D scene wants.
    /// </summary>
    public Vector3 Position { get; init; } = new(0f, 0f, -10f);

    /// <summary>
    /// The rectangle the following camera is kept inside - <c>X</c>/<c>Y</c> is one corner, <c>Width</c> and
    /// <c>Height</c> the extent from it. Required while <see cref="Follows"/> is true, where the framework
    /// builds the boundary the camera is clamped to; a rectangle of no width or no height is refused.
    /// </summary>
    public Rect? Bounds { get; init; }
}

/// <summary>
/// The music a map plays: the clip it starts with and the clip it loops, both built by
/// <c>IAssetFactory.TryCreateAudioClip</c> and named as such.
/// <para>
/// The game's own music package carries the two clips and nothing else, so there is no per map volume to
/// describe here: how loud the day scene's music is is the game's own setting.
/// </para>
/// </summary>
public sealed record DayMapMusic
{
    /// <summary>The clip played once when the map is entered; required.</summary>
    public AudioClipHandle? Intro { get; init; }

    /// <summary>The clip looped after the intro; required.</summary>
    public AudioClipHandle? Loop { get; init; }
}

/// <summary>
/// Everything one day scene map is built out of. A mod fills this in, hands it to
/// <see cref="IDayMapBuilder.TryBuild"/>, and the framework assembles the template, files it into the game's
/// asset pipeline and (once published) makes the day scene able to swap to it.
/// <para>
/// The collections are the map's own limits: at most 4096 palette entries, 32 tile layers, 100000 painted
/// cells, 4096 props, 4096 collision boxes, 256 spawn points and 100000 height cells. Coordinates are world
/// units within ±4096, the range the day scene's own map data stays inside.
/// </para>
/// </summary>
public sealed record DayMapSpec
{
    /// <summary>
    /// The key the built template is filed under in the game's asset pipeline, and the key a mod asks the map
    /// back by. It is process wide, so it must be namespaced by the mod that owns the map; must not be empty.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>
    /// The map's label: the key the day scene knows the map by (the <c>DataBaseDay</c> tables and the map
    /// language are keyed by it) and the prefix every spawn point's name is built from. Must not be empty.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>The size of one grid cell in world units; (1, 1), the size the game's own day maps use.</summary>
    public Vector2 CellSize { get; init; } = new(1f, 1f);

    /// <summary>The camera the map is entered with; required.</summary>
    public DayMapCamera? Camera { get; init; }

    /// <summary>The music the map plays; required.</summary>
    public DayMapMusic? Music { get; init; }

    /// <summary>
    /// The map's tile palette, keyed by <see cref="DayMapTile.Key"/>. Every cell and every prop names a tile
    /// through this list, so it is required even for a map that paints nothing.
    /// </summary>
    public IReadOnlyList<DayMapTile>? Tiles { get; init; }

    /// <summary>The map's tile layers, drawn in the order they are listed.</summary>
    public IReadOnlyList<DayMapLayer>? Layers { get; init; }

    /// <summary>The sprites standing on the map.</summary>
    public IReadOnlyList<DayMapProp>? Props { get; init; }

    /// <summary>What the player cannot walk through.</summary>
    public IReadOnlyList<DayMapCollision>? Collisions { get; init; }

    /// <summary>
    /// Where characters are put down on this map. Every point is built as an engine spawn marker object; the
    /// day scene also has to know the point, which is the database pass's side of the job (see
    /// <see cref="DayMapSpawnMarker"/>).
    /// </summary>
    public IReadOnlyList<DayMapSpawnMarker>? SpawnMarkers { get; init; }

    /// <summary>
    /// The label of the point a character without a named destination is put down on; must be one of
    /// <see cref="SpawnMarkers"/>.
    /// </summary>
    public string? DefaultSpawnMarker { get; init; }

    /// <summary>The height map, or null (or empty) for a flat map.</summary>
    public IReadOnlyList<DayMapHeightCell>? Height { get; init; }
}

/// <summary>
/// One day map the builder built: what the mod holds between building the map and publishing it, and the answer
/// to whether it is playable yet. A mod cannot build one itself - only <see cref="IDayMapBuilder.TryBuild"/>
/// hands one out.
/// </summary>
public sealed class DayMapHandle
{
    internal DayMapHandle(string label) => Label = label;

    /// <summary>The map's label, as the spec named it: the key the day scene's tables know the map by.</summary>
    public string Label { get; }

    /// <summary>
    /// Whether this map was published, i.e. whether the game's day map reference table names it. The table is
    /// rebuilt when the day database initializes, so a map published before that is not published any more;
    /// publishing again is how a mod keeps it named.
    /// </summary>
    public bool IsPublished { get; internal set; }
}

/// <summary>
/// Builds the day scene maps a mod ships, out of the values it describes them with, and publishes them as
/// maps the day scene can swap to.
/// <para>
/// Both steps create or reach engine objects - GameObjects with their components, the runtime asset table,
/// the game's own day map reference table - so both are main thread only: from a background thread hop with
/// <c>ICommonServices.MainThread</c> first. Nothing here throws over a map description that does not hold
/// together: a spec the engine cannot build is reported as <see langword="false"/>, and a refused spec never
/// reaches the engine, so a mod that got a field wrong stays in control of what happens next.
/// </para>
/// </summary>
public interface IDayMapBuilder
{
    /// <summary>
    /// Builds the map <paramref name="spec"/> describes and files the finished template into the game's asset
    /// pipeline under <see cref="DayMapSpec.Key"/>, so that the address the game resolves the map by exists.
    /// <para>
    /// The map is not playable yet: the day scene reaches a map through the reference
    /// <see cref="TryPublish"/> writes, which is what makes the two steps separate - a mod builds its maps
    /// while the databases are being collected (so a map the engine refused is not injected as data) and
    /// publishes them after the day database initialized (because that rebuilds the reference table).
    /// </para>
    /// </summary>
    /// <param name="spec">The map to build.</param>
    /// <param name="map">The built map, or null when the description was refused.</param>
    bool TryBuild(DayMapSpec spec, [NotNullWhen(true)] out DayMapHandle? map);

    /// <summary>
    /// Publishes a built map: the reference its template was filed under is written into the game's own day
    /// map reference table under the map's label, which is where the swap path takes the map it loads from.
    /// <para>
    /// The table is rebuilt by <c>DataBaseDay.Initialize</c>, so this belongs after the day database
    /// initialized; publishing the same map again simply writes it again. False when the map was never built
    /// by this builder, when the handle belongs to another map, or when the game's table is not there (yet).
    /// </para>
    /// </summary>
    /// <param name="map">A map this builder built.</param>
    bool TryPublish(DayMapHandle map);

    /// <summary>
    /// Whether a map with this label was built. A lookup over what this builder built, so it answers from any
    /// thread and never builds anything itself.
    /// </summary>
    /// <param name="label">The map's label.</param>
    bool IsBuilt(string label);
}
