using Mystia.Numerics;

namespace Mystia.Assets;

// What a day map description has to hold together before the engine is allowed to see it. Every rule here is
// decided without the engine - keys, counts, ranges, handles - which is what lets a mod that got a field
// wrong get a plain false back instead of a half built map, and what lets the rules be tested outside the
// game. The engine's own rules - whether a sorting layer with that name exists in this build, whether a
// handle is one this framework built - are the bridge's side of the same check.
internal static class DayMapValidation
{
    private const int MaximumTiles = 4096;
    private const int MaximumLayers = 32;
    private const int MaximumCells = 100000;
    private const int MaximumProps = 4096;
    private const int MaximumCollisions = 4096;
    private const int MaximumSpawnMarkers = 256;
    private const int MaximumHeightCells = 100000;
    private const int MaximumCoordinate = 4096;
    private const float MaximumSortedY = 1023f;

    /// <summary>
    /// The first rule <paramref name="spec"/> breaks, or null when the engine may build it. A reason is a
    /// sentence fragment the bridge traces with the map it belongs to.
    /// </summary>
    internal static string? Validate(DayMapSpec? spec)
    {
        if (spec is null)
            return "the spec is null";
        if (string.IsNullOrWhiteSpace(spec.Label))
            return "the label is empty";
        if (string.IsNullOrWhiteSpace(spec.Key))
            return "the asset key is empty";
        if (spec.Camera is null)
            return "the camera is missing";
        if (spec.Music is null)
            return "the music is missing";
        if (spec.Tiles is null)
            return "the tile palette is missing";
        if (spec.Layers is null)
            return "the tile layers are missing";
        if (spec.Props is null)
            return "the props are missing";
        if (spec.Collisions is null)
            return "the collisions are missing";
        if (spec.SpawnMarkers is null)
            return "the spawn markers are missing";
        if (!Finite(spec.CellSize) || spec.CellSize.X <= 0f || spec.CellSize.Y <= 0f)
            return "the cell size is not positive";

        if (spec.Camera is { } camera)
        {
            if (!Finite(camera.Position))
                return "the camera position is not finite";
            if (camera.Follows)
            {
                if (camera.Bounds is not { } bounds)
                    return "the camera follows but has no bounds";
                if (!Finite(bounds))
                    return "the camera bounds are not finite";
                if (bounds.Width <= 0f || bounds.Height <= 0f)
                    return "the camera bounds are empty";
            }
        }

        if (spec.Music is { } music)
        {
            if (music.Intro is null)
                return "the music has no intro clip";
            if (music.Loop is null)
                return "the music has no loop clip";
        }

        if (spec.Tiles.Count > MaximumTiles)
            return "the palette is larger than a map may hold";

        var tiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tile in spec.Tiles)
        {
            if (string.IsNullOrWhiteSpace(tile.Key))
                return "a tile has no key";
            if (tile.Sprite is null)
                return $"the tile '{tile.Key}' has no sprite";
            if (!tiles.Add(tile.Key!))
                return $"the tile key '{tile.Key}' is used twice";
        }

        if (spec.Layers.Count > MaximumLayers)
            return "the map has more layers than it may hold";

        var cells = 0;
        foreach (var layer in spec.Layers)
        {
            if (layer is null)
                return "a layer is null";
            if (string.IsNullOrWhiteSpace(layer.SortingLayer))
                return "a layer has no sorting layer";
            if (layer.SortingOrder is < short.MinValue or > short.MaxValue)
                return "a layer's sorting order is outside the renderer's range";
            if (layer.Cells is null)
                return "a layer has no cells";

            cells += layer.Cells.Count;
            if (cells > MaximumCells)
                return "the map paints more cells than it may hold";

            var painted = new HashSet<(int X, int Y)>();
            foreach (var cell in layer.Cells)
            {
                if (string.IsNullOrWhiteSpace(cell.Tile))
                    return "a cell names no tile";
                if (!tiles.Contains(cell.Tile!))
                    return $"a cell names the unknown tile '{cell.Tile}'";
                if (!Coordinate(cell.X, cell.Y))
                    return "a cell is outside the map";
                if (!painted.Add((cell.X, cell.Y)))
                    return "a layer paints the same cell twice";
                if (cell.Tint is { } tint && !Finite(tint))
                    return "a cell's tint is not finite";
                if (!Enum.IsDefined(cell.Rotation))
                    return "a cell's rotation is not one the engine turns";
            }
        }

        if (spec.Props.Count > MaximumProps)
            return "the map has more props than it may hold";

        foreach (var prop in spec.Props)
        {
            if (prop is null)
                return "a prop is null";
            if (string.IsNullOrWhiteSpace(prop.Tile))
                return "a prop names no tile";
            if (!tiles.Contains(prop.Tile!))
                return $"a prop names the unknown tile '{prop.Tile}'";
            if (!Coordinate(prop.Position))
                return "a prop is outside the map";
            if (!Finite(prop.Scale) || prop.Scale.X <= 0f || prop.Scale.Y <= 0f)
                return "a prop's scale is not positive";
            if (string.IsNullOrWhiteSpace(prop.SortingLayer))
                return "a prop has no sorting layer";
            if (prop.SortingOrder is < short.MinValue or > short.MaxValue)
                return "a prop's sorting order is outside the renderer's range";
            if (prop.SortByY && Math.Abs(prop.Position.Y) > MaximumSortedY)
                return "a y sorted prop is outside the range that sorting covers";
        }

        if (spec.Collisions.Count > MaximumCollisions)
            return "the map has more collision boxes than it may hold";

        foreach (var box in spec.Collisions)
        {
            if (!Coordinate(box.Position))
                return "a collision box is outside the map";
            if (!Finite(box.Size) || box.Size.X <= 0f || box.Size.Y <= 0f)
                return "a collision box is not positive";
        }

        if (spec.SpawnMarkers.Count > MaximumSpawnMarkers)
            return "the map has more spawn markers than it may hold";

        var markers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var marker in spec.SpawnMarkers)
        {
            if (string.IsNullOrWhiteSpace(marker.Label))
                return "a spawn marker has no label";
            if (!markers.Add(marker.Label!))
                return $"the spawn marker '{marker.Label}' is used twice";
            if (!Coordinate(marker.Position))
                return "a spawn marker is outside the map";
            if (marker.Radius is { } radius && (!float.IsFinite(radius) || radius <= 0f))
                return "a spawn marker's radius is not positive";
            if (!Enum.IsDefined(marker.Rotation))
                return "a spawn marker faces nowhere the engine knows";
            foreach (var box in spec.Collisions)
            {
                // A point inside a box would put the character into what the map says it cannot walk through,
                // which is a mistake in the description rather than something the engine could resolve.
                if (Math.Abs(marker.Position.X - box.Position.X) <= box.Size.X / 2f
                    && Math.Abs(marker.Position.Y - box.Position.Y) <= box.Size.Y / 2f)
                    return $"the spawn marker '{marker.Label}' stands inside a collision box";
            }
        }

        if (string.IsNullOrWhiteSpace(spec.DefaultSpawnMarker))
            return "the default spawn marker is missing";
        if (!markers.Contains(spec.DefaultSpawnMarker!))
            return $"the default spawn marker '{spec.DefaultSpawnMarker}' is not one of the map's";

        if (spec.Height is { } height)
        {
            if (height.Count > MaximumHeightCells)
                return "the height map has more cells than it may hold";

            var slopes = new HashSet<(int X, int Y)>();
            foreach (var cell in height)
            {
                if (!Coordinate(cell.X, cell.Y))
                    return "a height cell is outside the map";
                if (!float.IsFinite(cell.Slope) || Math.Abs(cell.Slope) > 1f)
                    return "a height cell's slope is outside -1..1";
                if (!slopes.Add((cell.X, cell.Y)))
                    return "the height map names the same cell twice";
            }
        }

        return null;
    }

    // Ranges are tested without Math.Abs: a coordinate of int.MinValue has no absolute value, and a spec must
    // never be able to throw out of the builder.
    private static bool Coordinate(int x, int y) =>
        x is >= -MaximumCoordinate and <= MaximumCoordinate
        && y is >= -MaximumCoordinate and <= MaximumCoordinate;

    private static bool Coordinate(Vector2 position) =>
        IsCoordinate(position.X) && IsCoordinate(position.Y);

    private static bool IsCoordinate(float value) =>
        float.IsFinite(value) && Math.Abs(value) <= MaximumCoordinate;

    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool Finite(Rect value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Width) && float.IsFinite(value.Height);

    private static bool Finite(Color value) =>
        float.IsFinite(value.R) && float.IsFinite(value.G) && float.IsFinite(value.B) && float.IsFinite(value.A);
}
