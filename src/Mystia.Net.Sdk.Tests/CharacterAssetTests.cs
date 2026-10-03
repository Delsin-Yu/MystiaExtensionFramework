using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Mystia.Assets;
using Mystia.Imgui;
using Mystia.Modding.Bridge;
using Mystia.Numerics;
using Mystia.Scenes;
using Xunit;

namespace Mystia.Tests;

// The character side of the asset surface: the texture questions a mod asks about art it already holds, and the
// pixel sprite set a character wears. What can be pinned without a running engine is pinned here — the shape of
// the contract (no engine type is ever named, no handle a mod can build itself), what the factory refuses, and
// the split between the two halves of a set (the frames a mod brings, the behaviour the game supplies). Building
// the game's own sprite set object, reading a texture's pixels through the engine and putting a set on a
// character are the engine's part of the job and can only be exercised in game.
public sealed class CharacterAssetTests
{
    private static readonly string[] BannedPrefixes = ["UnityEngine", "Il2Cpp", "Il2CppInterop"];

    /// <summary>
    /// The whole point of the facade: a mod may only name framework types, so neither the new asset types nor
    /// the character entries of the presentation service may name anything from the engine or the interop, value
    /// types included.
    /// </summary>
    [Theory]
    [InlineData(typeof(CharacterSpriteSetHandle))]
    [InlineData(typeof(CharacterHandle))]
    [InlineData(typeof(CharacterSpriteSetFrames))]
    [InlineData(typeof(CharacterSpriteSetStyle))]
    [InlineData(typeof(CharacterSpriteSetKind))]
    [InlineData(typeof(IPresentationServices))]
    public void Character_members_never_name_the_engine(Type contract)
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
    /// The new types are framework types, and the frames a set is built from are the same opaque sprite handles a
    /// mod files with the locator: one cut sprite serves both the asset pipeline and the character.
    /// </summary>
    [Fact]
    public void Character_surface_types_are_framework_types()
    {
        foreach (var type in new[]
                 {
                     typeof(CharacterSpriteSetHandle),
                     typeof(CharacterHandle),
                     typeof(CharacterSpriteSetFrames),
                     typeof(CharacterSpriteSetStyle),
                     typeof(CharacterSpriteSetKind),
                 })
        {
            Assert.Equal("Mystia.Assets", type.Namespace);
            Assert.Equal("Mystia.Net.Sdk", type.Assembly.GetName().Name);
        }

        foreach (var layer in new[] { nameof(CharacterSpriteSetFrames.Main), nameof(CharacterSpriteSetFrames.Eyes), nameof(CharacterSpriteSetFrames.Hair), nameof(CharacterSpriteSetFrames.Back) })
        {
            var property = typeof(CharacterSpriteSetFrames).GetProperty(layer)!;
            Assert.Equal(typeof(ReadOnlyMemory<SpriteHandle>), property.PropertyType);
        }
    }

    /// <summary>
    /// A handle is an identity, not a value: two handles are the same handle only when they are the same
    /// instance, so a mod can compare what it holds (and use a handle as a key) without either side being the
    /// engine object. It cannot build one, which is what keeps every set a character wears one the framework
    /// built.
    /// </summary>
    [Fact]
    public void A_handle_is_an_identity_a_mod_cannot_build()
    {
        foreach (var type in new[] { typeof(CharacterSpriteSetHandle), typeof(CharacterHandle) })
        {
            Assert.True(type.IsClass);
            Assert.True(type.IsAbstract);
            Assert.Null(type.GetConstructor(Type.EmptyTypes));
            Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        }

        var set = new UnityCharacterSpriteSetHandle(null!);
        var other = new UnityCharacterSpriteSetHandle(null!);
        var character = new UnityCharacterHandle(null!);

        Assert.Same(set, set);
        Assert.Equal(set, set);
        Assert.Equal(set.GetHashCode(), set.GetHashCode());
        Assert.NotSame(set, other);
        Assert.NotEqual(set, other);
        Assert.False(set.Equals(character));
        Assert.NotSame(set, character);

        // A handle is a key: what a mod holds in a table is the set itself, not a copy of it.
        var held = new HashSet<CharacterSpriteSetHandle> { set, other };
        Assert.Equal(2, held.Count);
        Assert.Contains(set, held);
    }

    /// <summary>
    /// The frames decide the layout and the style decides the behaviour, and both are refused when the game
    /// could not draw the result: a layer that does not carry the whole grid the animator indexes, a layer with
    /// a hole in it, art on a layer the set has none of (a compact set has no hair and no back), and a
    /// behaviour value that is not a number.
    /// </summary>
    [Fact]
    public void Factory_refuses_frames_the_animator_could_not_draw()
    {
        IAssetFactory factory = UnityAssetFactory.Shared;
        var body = Frames(12);
        var eyes = Frames(24);

        // A kind that is not one of the game's two sets.
        Assert.False(factory.TryCreateCharacterSpriteSet((CharacterSpriteSetKind)7, new CharacterSpriteSetFrames(body, eyes), CharacterSpriteSetStyle.Default, out var set));
        Assert.Null(set);

        // A set needs both of its layers, and a layer needs whole directions: 3 frames each for the body and 4
        // for the eyes, never fewer than the four directions and the six eye directions the animator asks for.
        Assert.False(factory.TryCreateCharacterSpriteSet(CharacterSpriteSetKind.Compact, new CharacterSpriteSetFrames(default, eyes), CharacterSpriteSetStyle.Default, out set));
        Assert.False(factory.TryCreateCharacterSpriteSet(CharacterSpriteSetKind.Compact, new CharacterSpriteSetFrames(body, default), CharacterSpriteSetStyle.Default, out set));
        Assert.False(factory.TryCreateCharacterSpriteSet(CharacterSpriteSetKind.Compact, new CharacterSpriteSetFrames(Frames(9), eyes), CharacterSpriteSetStyle.Default, out set));
        Assert.False(factory.TryCreateCharacterSpriteSet(CharacterSpriteSetKind.Compact, new CharacterSpriteSetFrames(Frames(13), eyes), CharacterSpriteSetStyle.Default, out set));
        Assert.False(factory.TryCreateCharacterSpriteSet(CharacterSpriteSetKind.Compact, new CharacterSpriteSetFrames(body, Frames(4)), CharacterSpriteSetStyle.Default, out set));
        Assert.False(factory.TryCreateCharacterSpriteSet(CharacterSpriteSetKind.Compact, new CharacterSpriteSetFrames(body, Frames(23)), CharacterSpriteSetStyle.Default, out set));
        Assert.False(factory.TryCreateCharacterSpriteSet(CharacterSpriteSetKind.Compact, new CharacterSpriteSetFrames(body, Frames(25)), CharacterSpriteSetStyle.Default, out set));

        // A hole in a layer: one step of the walk would draw nothing at all.
        var holed = Frames(12);
        holed[3] = null!;
        Assert.False(factory.TryCreateCharacterSpriteSet(CharacterSpriteSetKind.Compact, new CharacterSpriteSetFrames(holed, eyes), CharacterSpriteSetStyle.Default, out set));

        // The compact animator draws the body, the eyes and the trims only, so hair or back frames on a compact
        // set would be carried and never drawn; the layered set has all four layers.
        Assert.False(factory.TryCreateCharacterSpriteSet(CharacterSpriteSetKind.Compact, new CharacterSpriteSetFrames(body, eyes, Frames(12), default), CharacterSpriteSetStyle.Default, out set));
        Assert.False(factory.TryCreateCharacterSpriteSet(CharacterSpriteSetKind.Compact, new CharacterSpriteSetFrames(body, eyes, default, Frames(12)), CharacterSpriteSetStyle.Default, out set));
        Assert.False(factory.TryCreateCharacterSpriteSet(CharacterSpriteSetKind.Full, new CharacterSpriteSetFrames(body, eyes), CharacterSpriteSetStyle.Default, out set));
        Assert.False(factory.TryCreateCharacterSpriteSet(CharacterSpriteSetKind.Full, new CharacterSpriteSetFrames(body, eyes, Frames(12)), CharacterSpriteSetStyle.Default, out set));
        Assert.False(factory.TryCreateCharacterSpriteSet(CharacterSpriteSetKind.Full, new CharacterSpriteSetFrames(body, eyes, Frames(11), Frames(12)), CharacterSpriteSetStyle.Default, out set));

        // A behaviour value the animator would carry into the character's own transform.
        foreach (var style in new[]
                 {
                     new CharacterSpriteSetStyle { AnimationSpeedMultiplier = float.NaN },
                     new CharacterSpriteSetStyle { ExtraYOffset = float.PositiveInfinity },
                     new CharacterSpriteSetStyle { MoveSpeedMultiplier = float.NegativeInfinity },
                     new CharacterSpriteSetStyle { RotatePerTime = float.NaN },
                 })
        {
            Assert.False(factory.TryCreateCharacterSpriteSet(CharacterSpriteSetKind.Compact, new CharacterSpriteSetFrames(body, eyes), style, out set));
            Assert.Null(set);
        }
    }

    /// <summary>
    /// A set is only ever built out of sprites the factory cut: a frame handle that is not one of its own is
    /// refused rather than reached into, which is why a mod cannot hand a set the engine object it built itself.
    /// </summary>
    [Fact]
    public void Factory_only_accepts_sprites_it_built()
    {
        IAssetFactory factory = UnityAssetFactory.Shared;

        Assert.False(factory.TryCreateCharacterSpriteSet(
            CharacterSpriteSetKind.Compact,
            new CharacterSpriteSetFrames(Frames(12), Frames(24)),
            CharacterSpriteSetStyle.Default,
            out var set));
        Assert.Null(set);

        Assert.False(factory.TryCreateCharacterSpriteSet(
            CharacterSpriteSetKind.Full,
            new CharacterSpriteSetFrames(Frames(12), Frames(24), Frames(12), Frames(12)),
            CharacterSpriteSetStyle.Default,
            out set));
        Assert.Null(set);
    }

    /// <summary>
    /// The style is optional per member: unset means the game's own pixel art supplies the value, which is what a
    /// mod that only brought art wants, and a set that states one value keeps the game's value for every other.
    /// </summary>
    [Fact]
    public void A_style_states_only_what_it_changes()
    {
        var none = CharacterSpriteSetStyle.Default;
        Assert.Null(none.DoNotUseEyeSprite);
        Assert.Null(none.HasPrebakedShadow);
        Assert.Null(none.AnimationSpeedMultiplier);
        Assert.Null(none.ExtraYOffset);
        Assert.Null(none.MoveSpeedMultiplier);
        Assert.Null(none.IsHina);
        Assert.Null(none.RotatePerTime);
        Assert.Null(none.DoNotHaveStepVFX);

        // The frames of a compact set are the main and the eyes layer; hair and back are what a layered set adds.
        var frames = new CharacterSpriteSetFrames(Frames(12), Frames(24));
        Assert.Equal(12, frames.Main.Length);
        Assert.Equal(24, frames.Eyes.Length);
        Assert.True(frames.Hair.IsEmpty);
        Assert.True(frames.Back.IsEmpty);

        // One value stated, the rest still the game's: a spinning skin is the same frames with a different style.
        var spinning = new CharacterSpriteSetStyle { IsHina = true, RotatePerTime = 0.15f };
        Assert.True(spinning.IsHina);
        Assert.Equal(0.15f, spinning.RotatePerTime);
        Assert.Null(spinning.HasPrebakedShadow);
    }

    /// <summary>
    /// The two texture questions a mod needs about art it already holds: how big it is (a sprite sheet's layout
    /// is decided by its size) and what its pixels are (cutting one texture into another means reading the
    /// source). Both refuse a handle the framework did not build instead of throwing at it.
    /// </summary>
    [Fact]
    public void Texture_size_and_readback_refuse_a_handle_they_did_not_build()
    {
        IAssetFactory factory = UnityAssetFactory.Shared;

        Assert.False(factory.TryGetTextureSize(null!, out var width, out var height));
        Assert.Equal(0, width);
        Assert.Equal(0, height);

        Assert.False(factory.TryGetTextureSize(new ForeignTexture(), out width, out height));
        Assert.Equal(0, width);
        Assert.Equal(0, height);

        Assert.False(factory.TryReadPixels(null!, out var pixels));
        Assert.Null(pixels);
        Assert.False(factory.TryReadPixels(new ForeignTexture(), out pixels));
        Assert.Null(pixels);
    }

    /// <summary>The read back is a pixel buffer, not something new: the same buffer the factory builds paints and uploads it.</summary>
    [Fact]
    public void A_read_back_buffer_is_a_pixel_buffer()
    {
        Color[]? applied = null;
        var buffer = new PixelBuffer(2, 2, TextureHandle.White, written => applied = written.ToArray());

        buffer.Load([Color.Black, Color.White, new Color(1f, 0f, 0f), new Color(0f, 0f, 1f)]);
        Assert.Equal(Color.Black, buffer.Get(0, 0));
        Assert.Equal(Color.White, buffer.Get(1, 0));
        Assert.Equal(new Color(1f, 0f, 0f), buffer.Get(0, 1));

        buffer.Set(1, 1, Color.White);
        buffer.Fill(Color.Black);
        buffer.Apply();

        Assert.NotNull(applied);
        Assert.Equal(4, applied!.Length);
        Assert.All(applied, pixel => Assert.Equal(Color.Black, pixel));
    }

    /// <summary>The shape of the three new members: the out parameters a mod reads its answers from.</summary>
    [Fact]
    public void The_factory_carries_the_character_and_texture_members()
    {
        var size = typeof(IAssetFactory).GetMethod(nameof(IAssetFactory.TryGetTextureSize))!;
        Assert.Equal(typeof(bool), size.ReturnType);
        Assert.Equal(
            new[] { typeof(TextureHandle), typeof(int).MakeByRefType(), typeof(int).MakeByRefType() },
            size.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.True(size.GetParameters()[1].IsOut);
        Assert.True(size.GetParameters()[2].IsOut);

        var read = typeof(IAssetFactory).GetMethod(nameof(IAssetFactory.TryReadPixels))!;
        var pixels = read.GetParameters()[1];
        Assert.Equal(typeof(PixelBuffer), pixels.ParameterType.GetElementType());
        Assert.True(pixels.IsOut);
        Assert.Contains(pixels.GetCustomAttributes(inherit: false), attribute => attribute is NotNullWhenAttribute { ReturnValue: true });

        var create = typeof(IAssetFactory).GetMethod(nameof(IAssetFactory.TryCreateCharacterSpriteSet))!;
        Assert.Equal(
            new[]
            {
                typeof(CharacterSpriteSetKind),
                typeof(CharacterSpriteSetFrames),
                typeof(CharacterSpriteSetStyle),
                typeof(CharacterSpriteSetHandle).MakeByRefType(),
            },
            create.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Contains(create.GetParameters()[3].GetCustomAttributes(inherit: false), attribute => attribute is NotNullWhenAttribute { ReturnValue: true });
    }

    /// <summary>
    /// The character entries live on the scene scoped presentation service, the way the labels and the effects
    /// do: outside a scene loop they throw rather than act on whatever scene happens to be loaded, and inside one
    /// they answer a refusal of their own.
    /// </summary>
    [Fact]
    public void The_character_entries_are_scene_scoped()
    {
        var presentation = SplashSceneServices.Shared.Presentation;

        Assert.Throws<InvalidOperationException>(() => { presentation.BindCharacter(new object()); });
        Assert.Throws<InvalidOperationException>(() => { presentation.ApplyCharacterSprite(null!, null!); });

        ServiceScope.Enter();
        try
        {
            // Only a character can be wrapped, and only a handle the framework built can be put on one.
            Assert.Null(presentation.BindCharacter(null!));
            Assert.Null(presentation.BindCharacter(new object()));
            Assert.Null(presentation.BindCharacter("not a character"));
            Assert.False(presentation.ApplyCharacterSprite(null!, null!));
            Assert.False(presentation.ApplyCharacterSprite(new UnityCharacterHandle(null!), null!));
            Assert.False(presentation.ApplyCharacterSprite(new UnityCharacterHandle(null!), new UnityCharacterSpriteSetHandle(null!)));
        }
        finally
        {
            ServiceScope.Exit();
        }
    }

    /// <summary>The character half of the presentation contract is a handle in and a bool out, never an engine type.</summary>
    [Fact]
    public void Presentation_carries_the_character_contract()
    {
        Assert.Equal(
            typeof(CharacterHandle),
            typeof(IPresentationServices).GetMethod(nameof(IPresentationServices.BindCharacter))!.ReturnType);

        var apply = typeof(IPresentationServices).GetMethod(nameof(IPresentationServices.ApplyCharacterSprite))!;
        Assert.Equal(typeof(bool), apply.ReturnType);
        Assert.Equal(
            new[] { typeof(CharacterHandle), typeof(CharacterSpriteSetHandle), typeof(bool) },
            apply.GetParameters().Select(parameter => parameter.ParameterType));

        // Putting a set on a character is a "wear this now" call by default; the game's own no-op behaviour is
        // the opt out.
        var restart = apply.GetParameters()[2];
        Assert.True(restart.HasDefaultValue);
        Assert.Equal(true, restart.DefaultValue);
    }

    // A layer of the shape the game indexes, whose frames are not sprites this framework built: every refusal
    // that depends on the frames themselves is decided before the engine is reached, which is what lets these
    // checks run outside the game process.
    private static SpriteHandle[] Frames(int count)
    {
        var frames = new SpriteHandle[count];
        for (var index = 0; index < count; index++)
            frames[index] = new ForeignSprite();
        return frames;
    }

    private sealed class ForeignSprite : SpriteHandle
    {
    }

    private sealed class ForeignTexture : TextureHandle
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
