using System.Diagnostics.CodeAnalysis;
using DayScene;
using DayScene.Input;
using DayScene.Interactables;
using GameData.Core.Collections.DaySceneUtility;
using GameData.Profile;
using Mystia.Assets;
using Mystia.Data;
using UnityEngine;
using UnityEngine.Tilemaps;

using EngineReference = UnityEngine.AddressableAssets.AssetReference;

namespace Mystia.Modding.Bridge;

// The engine side of Mystia.Assets.IDayMapBuilder: everything a day scene map has to name a Unity or a game
// type for - the template GameObject and its components, the ScriptableObjects the map draws and plays from,
// the tilemaps, the spawn marker objects, the runtime asset table the finished template is filed into and the
// game's own day map reference table the published map is written to. This is the work a mod used to do in its
// own map registry; nothing module specific is left in it, so the map builder is the same for every mod and
// the mod only describes the map.
//
// The mod's own map registry (MetaMystia's DayMapRegistry) is the reference for what a map is made of, and
// this file is a transcription of it: a root GameObject carrying a DaySceneMap whose spawn marker and
// collectable fields are child transforms, the camera's follow flag, default position and boundary collider,
// a LoopedBGMPackage for the music, one Tile per palette entry cut from the sprites the factory built, one
// tilemap per layer, one SpriteRenderer per prop (with the y sorting controller where the prop wants one),
// one BoxCollider2D per collision box, one SpawnMarker per point, and the height map as a tilemap of one
// pixel slope sprites.
//
// What crosses the boundary is values only: the SpriteHandle and AudioClipHandle a mod holds are resolved to
// the engine objects the factory built them into, and the map's address is handed back as the framework's own
// AssetReference (a key and the address it is filed under).

/// <summary>
/// The engine side of the map builder: building a map's objects, filing the template and writing the game's
/// reference table. One implementation names engine types and lives in <c>Game/</c>; the builder holds the
/// rules and the state, so the same rules and the same state can be exercised without the engine running.
/// </summary>
internal interface IDayMapAssembler
{
    /// <summary>
    /// Builds the objects <paramref name="spec"/> describes and files the finished template into the game's
    /// runtime asset table under <see cref="DayMapSpec.Key"/>.
    /// </summary>
    /// <param name="spec">A description the engine free rules already accepted.</param>
    /// <param name="reference">The reference the template is filed under, or null when nothing was built.</param>
    /// <param name="reason">Why the engine refused, or null on success.</param>
    bool TryAssemble(DayMapSpec spec, [NotNullWhen(true)] out AssetReference? reference, out string? reason);

    /// <summary>Writes <paramref name="reference"/> into the game's day map reference table under the label.</summary>
    /// <param name="label">The map's label, the key the game's table uses.</param>
    /// <param name="reference">The reference the map's template is filed under.</param>
    /// <param name="reason">Why the game's table could not be written, or null on success.</param>
    bool TryPublish(string label, AssetReference reference, out string? reason);
}

/// <summary>
/// The builder reached through <c>ICommonServices.MapBuilder</c>. It decides everything that does not need
/// the engine (see <see cref="DayMapValidation"/>) and keeps what it built, so a mod may ask whether a map was
/// built without holding the handle, and may only publish a map this builder built.
/// </summary>
internal sealed class AssetDayMapBuilder(IDayMapAssembler assembler) : IDayMapBuilder
{
    /// <summary>The builder every mod reaches, on the engine side of the map path.</summary>
    internal static readonly AssetDayMapBuilder Shared = new(UnityDayMapAssembler.Shared);

    private sealed record Entry(DayMapHandle Handle, AssetReference Reference);

    // The tables are process wide and the builder is reached from the host's own threads as well as from mods,
    // so one lock guards what was built together. Building is main thread only, which is why holding the lock
    // across the engine call costs nothing.
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _maps = new(StringComparer.Ordinal);

    public bool TryBuild(DayMapSpec spec, [NotNullWhen(true)] out DayMapHandle? map)
    {
        map = null;
        if (DayMapValidation.Validate(spec) is { } refused)
        {
            GameBridgeHook.Trace($"DayMapBuilder: the map '{spec?.Label}' was not built: {refused}.");
            return false;
        }

        var label = spec.Label!;
        lock (_gate)
        {
            if (_maps.ContainsKey(label))
            {
                GameBridgeHook.Trace($"DayMapBuilder: the map '{label}' was already built; the second build was refused.");
                return false;
            }

            if (!assembler.TryAssemble(spec, out var reference, out var reason))
            {
                GameBridgeHook.Trace($"DayMapBuilder: the map '{label}' was not built: {reason}.");
                return false;
            }

            map = new DayMapHandle(label);
            _maps[label] = new Entry(map, reference!);
        }

        return true;
    }

    public bool TryPublish(DayMapHandle map)
    {
        if (map is null)
            return false;

        lock (_gate)
        {
            // Only the handle this builder handed out may publish what it built: a handle for a label another
            // builder built, or one a mod fabricated, names no template and no address.
            if (!_maps.TryGetValue(map.Label, out var entry) || !ReferenceEquals(entry.Handle, map))
            {
                GameBridgeHook.Trace($"DayMapBuilder: the map '{map.Label}' was not built here, so it was not published.");
                return false;
            }

            if (!assembler.TryPublish(map.Label, entry.Reference, out var reason))
            {
                GameBridgeHook.Trace($"DayMapBuilder: the map '{map.Label}' was not published: {reason}.");
                return false;
            }

            map.IsPublished = true;
            return true;
        }
    }

    public bool IsBuilt(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return false;

        lock (_gate)
            return _maps.ContainsKey(label);
    }
}

/// <summary>
/// The engine implementation of the map assembler. Everything it does reaches the engine, so it is main thread
/// only, and every failure - a sorting layer this build has not, a handle the framework did not build, the
/// engine refusing a call - is reported as a reason rather than thrown, with the map it had started destroyed.
/// </summary>
internal sealed class UnityDayMapAssembler : IDayMapAssembler
{
    internal static readonly UnityDayMapAssembler Shared = new();

    // Unity's built in layers are fixed slots: 0 Default, 1 TransparentFX, 2 Ignore Raycast, 4 Water, 5 UI.
    private const int IgnoreRaycastLayer = 2;

    public bool TryAssemble(DayMapSpec spec, [NotNullWhen(true)] out AssetReference? reference, out string? reason)
    {
        reference = null;
        if (!EngineRules(spec, out reason))
            return false;

        GameObject? root = null;
        try
        {
            root = new GameObject(spec.Label!);
            Build(spec, root);
        }
        catch (Exception error)
        {
            // A template that died halfway is destroyed whole: its root is hidden from the scene teardown, so
            // nothing else would ever release it, and its children go with it.
            if (root is not null)
                UnityEngine.Object.DestroyImmediate(root);
            reason = $"the engine refused the map: {error.GetBaseException().Message}";
            return false;
        }

        if (!AssetLocator.Shared.TryRegisterGameObject(spec.Key!, root!, out var filed))
        {
            // A template nobody can resolve is a map nobody can enter.
            UnityEngine.Object.DestroyImmediate(root!);
            reason = "the runtime asset table has nothing that serves a map template";
            return false;
        }

        reference = filed!;
        return true;
    }

    public bool TryPublish(string label, AssetReference reference, out string? reason)
    {
        reason = null;
        var table = DataBaseDay.mapReference;
        if (table is null)
        {
            reason = "the day map reference table is not initialized";
            return false;
        }

        // The address the framework filed the template under is a GUID, which is what the engine's own
        // reference is built from and what the swap path resolves through the runtime locator. The reference
        // is the type the game's own day map node carries, so the table keeps what it always held.
        table[label] = new EngineReference(reference.Address);
        return true;
    }

    /// <summary>
    /// The rules that cannot be decided without the engine: the sorting layers the map's renderers are filed
    /// under, and the handles the framework built. They run before anything is created, so a description the
    /// engine would refuse costs no objects.
    /// </summary>
    private static bool EngineRules(DayMapSpec spec, out string? reason)
    {
        reason = null;
        foreach (var layer in spec.Layers!)
        {
            if (!SortingLayerExists(layer.SortingLayer!))
            {
                reason = $"this build has no sorting layer named '{layer.SortingLayer}'";
                return false;
            }
        }

        foreach (var prop in spec.Props!)
        {
            if (!SortingLayerExists(prop.SortingLayer!))
            {
                reason = $"this build has no sorting layer named '{prop.SortingLayer}'";
                return false;
            }
        }

        foreach (var tile in spec.Tiles!)
        {
            if (tile.Sprite is not UnitySpriteHandle { Sprite: not null })
            {
                reason = $"the tile '{tile.Key}' holds a sprite this framework did not build";
                return false;
            }
        }

        if (spec.Music!.Intro is not UnityAudioClipHandle { Clip: not null }
            || spec.Music.Loop is not UnityAudioClipHandle { Clip: not null })
        {
            reason = "the map's music holds a clip this framework did not build";
            return false;
        }

        return true;
    }

    // Whether the running game has a sorting layer of that name. A renderer's sorting layer is named by a
    // string the engine resolves against its own table, and it silently keeps the default layer when the name
    // names nothing - which would file the map's art under the wrong layer. The shipped metadata has no way to
    // ask the table directly (the player stripped the engine's own SortingLayer.NameToID and the renderer's own
    // sortingLayerName reader, like it stripped the image loader), so the question is asked of a sorting group,
    // whose own name reader this build does carry: set the name and read it back. A name the engine does not
    // know does not read back as itself.
    private static bool SortingLayerExists(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        var probe = new GameObject("MystiaSortingLayerProbe");
        try
        {
            var group = probe.AddComponent<UnityEngine.Rendering.SortingGroup>();
            group.sortingLayerName = name;
            return group.sortingLayerName == name;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(probe);
        }
    }

    private static void Build(DayMapSpec spec, GameObject root)
    {
        var camera = spec.Camera!;
        root.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(root);

        var map = root.AddComponent<DaySceneMap>();
        map.spawnMarkerField = Child("SpawnMarkers", root.transform).transform;
        map.collectableField = Child("Collectables", root.transform).transform;
        map.shouldCameraFollow = camera.Follows;
        map.cameraDefaultPosition = new Vector3(camera.Position.X, camera.Position.Y, camera.Position.Z);
        if (camera.Follows)
        {
            var boundary = Child("CameraBounds", root.transform);
            // Unity's built in layers are fixed and cannot be renamed - 2 is Ignore Raycast in every build -
            // and this player's stripped metadata has no LayerMask.NameToLayer to ask (the same stripping that
            // took the engine's image loader), so the slot is named directly. The game's own camera boundary
            // sits on that layer so that the clamped boundary collider is not what the player's movement and
            // interaction queries hit.
            boundary.layer = IgnoreRaycastLayer;

            // The boundary is the collider the camera is clamped by, so it is the rectangle's four corners as a
            // polygon - the same shape the game's own maps use.
            var bounds = camera.Bounds!.Value;
            var polygon = boundary.AddComponent<PolygonCollider2D>();
            polygon.SetPath(0, new Vector2[]
            {
                new(bounds.X, bounds.Y),
                new(bounds.X, bounds.YMax),
                new(bounds.XMax, bounds.YMax),
                new(bounds.XMax, bounds.Y),
            });
            map.boundingShape = polygon;
        }

        var music = spec.Music!;
        var package = ScriptableObject.CreateInstance<LoopedBGMPackage>();
        package.hideFlags = HideFlags.HideAndDontSave;
        package.intro = ((UnityAudioClipHandle)music.Intro!).Clip;
        package.loop = ((UnityAudioClipHandle)music.Loop!).Clip;
        map.mapBGM = package;

        var palette = Palette(spec);
        var grid = Child("Grid", root.transform).AddComponent<Grid>();
        grid.cellSize = new Vector3(spec.CellSize.X, spec.CellSize.Y, 0f);
        if (spec.Height is { Count: > 0 } height)
            map.height = BuildHeight(height, grid.transform);

        Paint(spec, grid.transform, palette);
        Place(spec, root.transform, palette);
        Mark(spec, map);
    }

    // One engine tile per palette entry, out of the sprite the factory cut. The engine tile carries the
    // collider type the game's own day maps use (none: what stops the player is a collision box), and the
    // palette's key names the tile object rather than the sprite, which the mod may draw with elsewhere.
    private static Dictionary<string, Tile> Palette(DayMapSpec spec)
    {
        var palette = new Dictionary<string, Tile>(StringComparer.Ordinal);
        foreach (var entry in spec.Tiles!)
        {
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = entry.Key;
            tile.hideFlags = HideFlags.HideAndDontSave;
            tile.sprite = ((UnitySpriteHandle)entry.Sprite!).Sprite;
            tile.colliderType = Tile.ColliderType.None;
            palette.Add(entry.Key!, tile);
        }

        return palette;
    }

    private static void Paint(DayMapSpec spec, Transform grid, Dictionary<string, Tile> palette)
    {
        foreach (var layer in spec.Layers!)
        {
            var host = Child(layer.Name ?? "Tilemap", grid);
            var tilemap = host.AddComponent<Tilemap>();
            // The game's own day maps anchor a tile at the cell's own origin, which is what keeps a tile's
            // rect and a collision box on the same corner.
            tilemap.tileAnchor = Vector3.zero;
            var renderer = host.AddComponent<TilemapRenderer>();
            renderer.sortingLayerName = layer.SortingLayer!;
            renderer.sortingOrder = layer.SortingOrder;

            foreach (var cell in layer.Cells!)
            {
                var at = new Vector3Int(cell.X, cell.Y, 0);
                tilemap.SetTile(at, palette[cell.Tile!]);
                if (cell.Tint is { } tint)
                    tilemap.SetColor(at, new Color(tint.R, tint.G, tint.B, tint.A));
                if (cell.Rotation != DayMapTileRotation.None)
                    tilemap.SetTransformMatrix(at, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, Degrees(cell.Rotation)), Vector3.one));
            }

            // The layer only draws what was painted, so the tilemap's own bounds are the painted cells.
            tilemap.CompressBounds();
        }
    }

    private static void Place(DayMapSpec spec, Transform root, Dictionary<string, Tile> palette)
    {
        foreach (var prop in spec.Props!)
        {
            var host = Child(prop.Name ?? prop.Tile!, root);
            host.transform.localPosition = new Vector3(prop.Position.X, prop.Position.Y, 0f);
            host.transform.localScale = new Vector3(prop.Scale.X, prop.Scale.Y, 1f);
            var renderer = host.AddComponent<SpriteRenderer>();
            renderer.sprite = palette[prop.Tile!].sprite;
            renderer.sortingLayerName = prop.SortingLayer!;
            renderer.sortingOrder = prop.SortingOrder;
            if (prop.SortByY)
                host.AddComponent<DEYU.Utils.LayerSortingController>();
        }

        foreach (var box in spec.Collisions!)
        {
            var host = Child(box.Name ?? "Collision", root);
            host.transform.localPosition = new Vector3(box.Position.X, box.Position.Y, 0f);
            host.AddComponent<BoxCollider2D>().size = new Vector2(box.Size.X, box.Size.Y);
        }
    }

    private static void Mark(DayMapSpec spec, DaySceneMap map)
    {
        foreach (var point in spec.SpawnMarkers!)
        {
            var name = MarkerName(spec.Label!, point.Label);
            var host = Child(name, map.spawnMarkerField);
            host.transform.localPosition = new Vector3(point.Position.X, point.Position.Y, 0f);
            var marker = host.AddComponent<SpawnMarker>();
            marker.spawnMarkerName = name;
            marker.targetRotation = (DayScenePlayerInputGenerator.CharacterRotation)(int)point.Rotation;
            if (point.Radius is { } radius)
            {
                // The marker's own reach: the component carries a radius and the flag that makes it use that
                // radius instead of the one it was built with.
                marker.shouldOverrideRadius = true;
                marker.overrideRadius = radius;
            }
        }
    }

    // A point's engine object is named the way the database pass names the point, so the name the map builds
    // and the name the map's marker label set holds are the same one: GameRecords.Marker is that join, and it
    // is what the day scene resolves a marker by. The point's own geometry has nothing to do with its name.
    private static string MarkerName(string label, string? point) =>
        GameRecords.Marker(label, new SpawnMarkerData { Label = point });

    // The height map, as the game's own maps carry it: a one pixel sprite per distinct slope, sampled out of a
    // two row texture whose red channel is the magnitude and whose green channel is the sign, in a tilemap
    // with no renderer (only the player controller reads it). The sprite is one pixel wide, so a cell's own
    // slope is what the sampler gets.
    private static Tilemap BuildHeight(IReadOnlyList<DayMapHeightCell> cells, Transform grid)
    {
        var texture = new Texture2D(256, 2, TextureFormat.RGBA32, false);
        texture.name = "HeightSlopes";
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[512];
        for (var x = 0; x < 256; x++)
        {
            pixels[x] = new Color(x / 255f, 0f, 0f, 1f);
            pixels[256 + x] = new Color(x / 255f, 1f, 0f, 1f);
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);

        var map = Child("HEIGHT_MAP", grid).AddComponent<Tilemap>();
        map.tileAnchor = Vector3.zero;
        var tiles = new Dictionary<(int Sign, int Magnitude), Tile>();
        foreach (var cell in cells)
        {
            var magnitude = Mathf.RoundToInt(Math.Abs(cell.Slope) * 255f);
            if (magnitude == 0)
                continue;

            var sign = cell.Slope > 0f ? 1 : 0;
            if (!tiles.TryGetValue((sign, magnitude), out var tile))
            {
                var sprite = Sprite.Create(texture, new Rect(magnitude, sign, 1f, 1f), Vector2.zero, 1f);
                sprite.hideFlags = HideFlags.HideAndDontSave;
                tile = ScriptableObject.CreateInstance<Tile>();
                tile.name = $"Slope_{cell.Slope}";
                tile.hideFlags = HideFlags.HideAndDontSave;
                tile.sprite = sprite;
                tile.colliderType = Tile.ColliderType.None;
                tiles.Add((sign, magnitude), tile);
            }

            map.SetTile(new Vector3Int(cell.X, cell.Y, 0), tile);
        }

        map.CompressBounds();
        return map;
    }

    private static float Degrees(DayMapTileRotation rotation) => rotation switch
    {
        DayMapTileRotation.Degrees90 => 90f,
        DayMapTileRotation.Degrees180 => 180f,
        DayMapTileRotation.Degrees270 => 270f,
        _ => 0f,
    };

    private static GameObject Child(string name, Transform parent)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }
}
