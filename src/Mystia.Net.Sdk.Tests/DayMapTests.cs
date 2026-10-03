using System.Reflection;
using Mystia.Assets;
using Mystia.Data;
using Mystia.Modding.Bridge;
using Mystia.Numerics;
using Mystia.Scenes;
using Xunit;

namespace Mystia.Tests;

// The day map surface is where a mod stops assembling its own map template: it describes the map, the framework
// builds the engine objects, files the template and writes the game's own day map reference table. These tests
// pin everything the builder decides without the engine - the shape of the contract, what it refuses, and the
// two steps a map goes through - by standing in for the engine side with a plain object. Whether the engine
// accepts the template it is handed is the part only the running game can answer.
public sealed class DayMapTests
{
    private static readonly string[] BannedPrefixes = ["UnityEngine", "Il2Cpp", "Il2CppInterop"];

    private const string Label = "mystia.tests/daymap";
    private const string Key = "mystia.tests/daymap/asset";

    /// <summary>
    /// The whole point of the facade: a mod only ever names framework types, so no member of the builder or of
    /// the records it takes may name anything from the engine or the interop, value types included.
    /// </summary>
    [Theory]
    [InlineData(typeof(IDayMapBuilder))]
    [InlineData(typeof(DayMapSpec))]
    [InlineData(typeof(DayMapHandle))]
    [InlineData(typeof(DayMapCamera))]
    [InlineData(typeof(DayMapMusic))]
    [InlineData(typeof(DayMapTile))]
    [InlineData(typeof(DayMapCell))]
    [InlineData(typeof(DayMapLayer))]
    [InlineData(typeof(DayMapProp))]
    [InlineData(typeof(DayMapCollision))]
    [InlineData(typeof(DayMapSpawnMarker))]
    [InlineData(typeof(DayMapHeightCell))]
    [InlineData(typeof(DayMapTileRotation))]
    public void Day_map_members_never_name_the_engine(Type contract)
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
    /// The types a mod holds and fills in are framework types too - the map description carries the asset
    /// handles the factory hands out, and the map itself is the framework's own opaque handle - so nothing a
    /// mod passes into the builder can be an engine object it built itself.
    /// </summary>
    [Fact]
    public void Day_map_types_are_framework_types()
    {
        foreach (var type in new[]
                 {
                     typeof(IDayMapBuilder),
                     typeof(DayMapSpec),
                     typeof(DayMapHandle),
                     typeof(DayMapCamera),
                     typeof(DayMapMusic),
                     typeof(DayMapTile),
                     typeof(DayMapCell),
                     typeof(DayMapLayer),
                     typeof(DayMapProp),
                     typeof(DayMapCollision),
                     typeof(DayMapSpawnMarker),
                     typeof(DayMapHeightCell),
                 })
        {
            Assert.Equal("Mystia", type.Namespace?.Split('.')[0]);
            Assert.Equal("Mystia.Net.Sdk", type.Assembly.GetName().Name);
        }

        // The description is built out of the handles the factory produces, not out of anything a mod owns.
        Assert.Equal(typeof(SpriteHandle), typeof(DayMapTile).GetProperty(nameof(DayMapTile.Sprite))!.PropertyType);
        Assert.Equal(typeof(AudioClipHandle), typeof(DayMapMusic).GetProperty(nameof(DayMapMusic.Loop))!.PropertyType);
    }

    /// <summary>
    /// The entry point: the map builder hangs off the always available services next to the asset surface it is
    /// built from, and the services object every mod reaches hands out the bridge's own builder.
    /// </summary>
    [Fact]
    public void Common_services_carry_the_map_builder()
    {
        Assert.Equal(typeof(IDayMapBuilder), typeof(ICommonServices).GetProperty(nameof(ICommonServices.MapBuilder))!.PropertyType);
        Assert.Same(AssetDayMapBuilder.Shared, CommonServices.Shared.MapBuilder);
    }

    /// <summary>
    /// Building a map and publishing it are two steps, and the builder answers for both: a map is built once,
    /// the handle it handed out is the only thing that may publish it, and publishing is what makes it known to
    /// the game. Nothing here reaches the engine - the assembler is stood in for - so the rules of that state
    /// machine are pinned without a running game.
    /// </summary>
    [Fact]
    public void A_map_is_built_once_and_published_through_its_own_handle()
    {
        var assembler = new FakeAssembler();
        var builder = new AssetDayMapBuilder(assembler);

        // Before anything was built: a plain lookup, and nothing to publish.
        Assert.False(builder.IsBuilt(Label));
        Assert.False(builder.IsBuilt(""));
        Assert.False(builder.IsBuilt(null!));
        Assert.False(builder.TryPublish(new DayMapHandle(Label)));
        Assert.False(builder.TryPublish(null!));

        Assert.True(builder.TryBuild(Valid(), out var map));
        Assert.NotNull(map);
        Assert.Equal(Label, map.Label);
        Assert.False(map.IsPublished);
        Assert.True(builder.IsBuilt(Label));
        Assert.Equal(new[] { Label }, assembler.Assembled);

        // A label is built once: the same map is refused, and the engine is not asked again.
        Assert.False(builder.TryBuild(Valid(), out var again));
        Assert.Null(again);
        Assert.Equal(new[] { Label }, assembler.Assembled);
        Assert.False(builder.TryBuild(Valid() with { Key = "mystia.tests/other" }, out _));

        // Publishing writes the reference the engine filed the template under.
        Assert.True(builder.TryPublish(map));
        Assert.True(map.IsPublished);
        Assert.Same(assembler.Reference, Assert.Single(assembler.Published));

        // A handle that names this map but is not the one that was handed out is not this map.
        Assert.False(builder.TryPublish(new DayMapHandle(Label)));
        Assert.False(builder.TryPublish(new DayMapHandle("mystia.tests/never-built")));

        // Publishing again simply writes it again: the game's table is rebuilt when the day database
        // initializes, so a mod publishes what it built again after that.
        Assert.True(builder.TryPublish(map));
        Assert.Equal(2, assembler.Published.Count);
    }

    /// <summary>
    /// A description the engine refuses is a map that was not built: the label stays free, no handle is handed
    /// out, and the same label may be built again once the engine can take it. A map the engine built but the
    /// game's reference table cannot name is built and unpublished, which is what the state reports.
    /// </summary>
    [Fact]
    public void A_map_the_engine_or_the_table_refuses_is_reported()
    {
        var assembler = new FakeAssembler { Accept = false, Reason = "this build has no sorting layer named 'Nope'" };
        var builder = new AssetDayMapBuilder(assembler);

        Assert.False(builder.TryBuild(Valid(), out var refused));
        Assert.Null(refused);
        Assert.False(builder.IsBuilt(Label));

        // The description was fine, so it did reach the engine - and the label was not consumed by the refusal.
        Assert.Equal(new[] { Label }, assembler.Assembled);
        assembler.Accept = true;
        Assert.True(builder.TryBuild(Valid(), out var map));

        // The day map reference table is rebuilt by the day database, so a map published before it exists is
        // built but not playable - the query says which.
        assembler.TableReady = false;
        Assert.False(builder.TryPublish(map));
        Assert.False(map.IsPublished);

        assembler.TableReady = true;
        Assert.True(builder.TryPublish(map));
        Assert.True(map.IsPublished);
    }

    /// <summary>
    /// Bad input is reported, never thrown, and a description the framework refuses never reaches the engine:
    /// every one of these refusals is decided before the first engine call, which is what lets the whole list
    /// run outside the game process. The last few entries are the ones the engine would otherwise pay for -
    /// zero sized geometry, a negative radius and a tile with no sprite.
    /// </summary>
    [Fact]
    public void The_builder_refuses_a_description_it_cannot_build()
    {
        var layer = Valid().Layers![0];
        var refusals = new List<(string What, DayMapSpec? Spec)>
        {
            ("no description at all", null),
            ("a label that is only whitespace", Valid() with { Label = "  " }),
            ("no label", Valid() with { Label = null }),
            ("no asset key", Valid() with { Key = null }),
            ("no camera", Valid() with { Camera = null }),
            ("no music", Valid() with { Music = null }),
            ("no tile palette", Valid() with { Tiles = null }),
            ("no tile layers", Valid() with { Layers = null }),
            ("no props", Valid() with { Props = null }),
            ("no collisions", Valid() with { Collisions = null }),
            ("no spawn markers", Valid() with { SpawnMarkers = null }),
            ("a cell size of zero", Valid() with { CellSize = new Vector2(0f, 1f) }),
            ("a cell size that is not finite", Valid() with { CellSize = new Vector2(float.NaN, 1f) }),
            ("a following camera without bounds", Valid() with { Camera = new DayMapCamera { Follows = true } }),
            ("camera bounds of no height", Valid() with { Camera = new DayMapCamera { Bounds = new Rect(0f, 0f, 10f, 0f) } }),
            ("a camera position that is not finite", Valid() with { Camera = new DayMapCamera { Follows = false, Position = new Vector3(float.PositiveInfinity, 0f, 0f) } }),
            ("music without an intro clip", Valid() with { Music = new DayMapMusic { Loop = new FakeClip() } }),
            ("music without a loop clip", Valid() with { Music = new DayMapMusic { Intro = new FakeClip() } }),
            ("a tile without a key", Valid() with { Tiles = [new DayMapTile("", new FakeSprite())] }),
            ("a tile key used twice", Valid() with { Tiles = [new DayMapTile("floor", new FakeSprite()), new DayMapTile("floor", new FakeSprite())] }),
            ("a tile without a sprite", Valid() with { Tiles = [new DayMapTile("floor", null)] }),
            ("a layer without a sorting layer", Valid() with { Layers = [layer with { SortingLayer = null }] }),
            ("a layer without cells", Valid() with { Layers = [layer with { Cells = null }] }),
            ("a layer with a sorting order outside the renderer's range", Valid() with { Layers = [layer with { SortingOrder = 32768 }] }),
            ("a cell naming a tile the palette does not hold", Valid() with { Layers = [layer with { Cells = [new DayMapCell(0, 0, "missing")] }] }),
            ("a cell outside the map", Valid() with { Layers = [layer with { Cells = [new DayMapCell(5000, 0, "floor")] }] }),
            ("a layer painting the same cell twice", Valid() with { Layers = [layer with { Cells = [new DayMapCell(0, 0, "floor"), new DayMapCell(0, 0, "floor")] }] }),
            ("a cell tint that is not a colour", Valid() with { Layers = [layer with { Cells = [new DayMapCell(0, 0, "floor") { Tint = new Color(0f, float.NaN, 0f) }] }] }),
            ("a cell rotation the engine cannot turn", Valid() with { Layers = [layer with { Cells = [new DayMapCell(0, 0, "floor") { Rotation = (DayMapTileRotation)9 }] }] }),
            ("a prop naming a tile the palette does not hold", Valid() with { Props = [Prop() with { Tile = "missing" }] }),
            ("a prop outside the map", Valid() with { Props = [Prop() with { Position = new Vector2(0f, 5000f) }] }),
            ("a prop scaled to nothing", Valid() with { Props = [Prop() with { Scale = new Vector2(1f, 0f) }] }),
            ("a prop scaled negatively", Valid() with { Props = [Prop() with { Scale = new Vector2(-1f, 1f) }] }),
            ("a prop without a sorting layer", Valid() with { Props = [Prop() with { SortingLayer = null }] }),
            ("a prop sorted by y outside the range sorting covers", Valid() with { Props = [Prop() with { Position = new Vector2(0f, 2000f) }] }),
            ("a collision box outside the map", Valid() with { Collisions = [new DayMapCollision("wall", new Vector2(0f, 9000f), new Vector2(1f, 1f))] }),
            ("a collision box of no width", Valid() with { Collisions = [new DayMapCollision("wall", new Vector2(-4f, 0f), new Vector2(0f, 1f))] }),
            ("a collision box of negative height", Valid() with { Collisions = [new DayMapCollision("wall", new Vector2(-4f, 0f), new Vector2(1f, -1f))] }),
            ("a spawn marker without a label", Valid() with { SpawnMarkers = [new DayMapSpawnMarker("", new Vector2(0f, 0f), CharacterRotationKind.Down)] }),
            ("a spawn marker used twice", Valid() with { SpawnMarkers = [Marker(), Marker()] }),
            ("a spawn marker outside the map", Valid() with { SpawnMarkers = [new DayMapSpawnMarker("entrance", new Vector2(0f, -9000f), CharacterRotationKind.Down)], DefaultSpawnMarker = "entrance" }),
            ("a spawn marker with a negative radius", Valid() with { SpawnMarkers = [Marker() with { Radius = -1f }] }),
            ("a spawn marker with a radius of zero", Valid() with { SpawnMarkers = [Marker() with { Radius = 0f }] }),
            ("a spawn marker facing nowhere", Valid() with { SpawnMarkers = [new DayMapSpawnMarker("entrance", new Vector2(0f, 0f), (CharacterRotationKind)7)], DefaultSpawnMarker = "entrance" }),
            ("a spawn marker standing inside a collision box", Valid() with { SpawnMarkers = [new DayMapSpawnMarker("entrance", new Vector2(-4f, 0f), CharacterRotationKind.Down)], DefaultSpawnMarker = "entrance" }),
            ("no default spawn marker", Valid() with { DefaultSpawnMarker = null }),
            ("a default spawn marker the map does not have", Valid() with { DefaultSpawnMarker = "nowhere" }),
            ("a height cell with a slope outside -1..1", Valid() with { Height = [new DayMapHeightCell(0, 0, 2f)] }),
            ("a height cell with a slope that is not a number", Valid() with { Height = [new DayMapHeightCell(0, 0, float.NaN)] }),
            ("a height cell outside the map", Valid() with { Height = [new DayMapHeightCell(-9000, 0, 0.5f)] }),
            ("the height map naming the same cell twice", Valid() with { Height = [new DayMapHeightCell(0, 0, 0.5f), new DayMapHeightCell(0, 0, -0.5f)] }),
            ("more layers than a map may hold", Valid() with { Layers = Enumerable.Repeat(layer, 33).ToArray() }),
        };

        var assembler = new FakeAssembler();
        var builder = new AssetDayMapBuilder(assembler);
        foreach (var (what, spec) in refusals)
        {
            Assert.False(builder.TryBuild(spec!, out var map), what);
            Assert.Null(map);
        }

        // Not one of them reached the engine: a refusal costs no objects, and the game is never asked.
        Assert.Empty(assembler.Assembled);

        // The description every refusal was cut down from is the one that builds.
        Assert.True(builder.TryBuild(Valid(), out _));
        Assert.Equal(new[] { Label }, assembler.Assembled);
    }

    /// <summary>
    /// The engine members the map path names, and the one thing about them the compiler cannot see: a spawn
    /// point's facing is the framework's own enum stored by value into the game's rotation enum. Everything
    /// here is metadata reflection, so it runs with the interop and before the game is touched.
    /// </summary>
    [Fact]
    public void The_map_path_pins_the_engine_it_names() => DayMapTargets.Verify();

    private static DayMapSpec Valid() => new()
    {
        Key = Key,
        Label = Label,
        CellSize = new Vector2(1f, 1f),
        Camera = new DayMapCamera
        {
            Follows = true,
            Position = new Vector3(0f, 0f, -10f),
            Bounds = new Rect(-10f, -8f, 20f, 16f),
        },
        Music = new DayMapMusic { Intro = new FakeClip(), Loop = new FakeClip() },
        Tiles = [new DayMapTile("floor", new FakeSprite()), new DayMapTile("prop", new FakeSprite())],
        Layers =
        [
            new DayMapLayer
            {
                Name = "Floor",
                SortingLayer = "Background",
                SortingOrder = -2000,
                Cells = [new DayMapCell(0, 0, "floor"), new DayMapCell(1, 0, "floor")],
            },
        ],
        Props =
        [
            new DayMapProp
            {
                Name = "tree",
                Tile = "prop",
                Position = new Vector2(2f, 3f),
                SortingLayer = "Character",
                SortingOrder = 0,
            },
        ],
        Collisions = [new DayMapCollision("wall", new Vector2(-4f, 0f), new Vector2(1f, 1f))],
        SpawnMarkers = [Marker(), new DayMapSpawnMarker("exit", new Vector2(3f, 0f), CharacterRotationKind.Up)],
        DefaultSpawnMarker = "entrance",
        Height = [new DayMapHeightCell(0, 0, 0.5f)],
    };

    private static DayMapSpawnMarker Marker() => new("entrance", new Vector2(0f, 0f), CharacterRotationKind.Down);

    private static DayMapProp Prop() => new()
    {
        Name = "tree",
        Tile = "prop",
        Position = new Vector2(2f, 3f),
        SortingLayer = "Character",
        SortingOrder = 0,
    };

    // The engine side, stood in for by a plain object: the builder's rules and its state are what the tests are
    // about, and the assembler only has to say what the engine said. The reference it hands back stands for the
    // address the runtime asset table filed the template under.
    private sealed class FakeAssembler : IDayMapAssembler
    {
        internal List<string> Assembled { get; } = [];

        internal List<AssetReference> Published { get; } = [];

        internal AssetReference? Reference { get; } = new(Key, "0f14e45f-ceea-167a-5a36-dedd4bea2543");

        internal bool Accept { get; set; } = true;

        internal bool TableReady { get; set; } = true;

        internal string Reason { get; set; } = "the engine refused the map";

        public bool TryAssemble(DayMapSpec spec, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AssetReference? reference, out string? reason)
        {
            Assembled.Add(spec.Label ?? "");
            reason = Accept ? null : Reason;
            reference = Accept ? Reference : null;
            return Accept;
        }

        public bool TryPublish(string label, AssetReference reference, out string? reason)
        {
            reason = TableReady ? null : "the day map reference table is not initialized";
            if (TableReady)
                Published.Add(reference);
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
