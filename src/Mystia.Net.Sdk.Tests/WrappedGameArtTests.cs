using Mystia.Assets;
using Mystia.Modding.Bridge;
using Xunit;

namespace Mystia.Tests;

/// <summary>
/// The two directions of "what the game already holds": a sprite the game loaded wrapped as a handle, and a
/// character pixel set the game loaded read apart into the frames and the style a mod would build one from.
/// Only the refusals are testable without the engine running - a real sprite cannot be built here - which is
/// exactly the half a mod acts on when it hands over the wrong object.
/// </summary>
public sealed class WrappedGameArtTests
{
    private static readonly IAssetFactory Factory = UnityAssetFactory.Shared;

    [Fact]
    public void Wrapping_a_sprite_refuses_what_is_not_one()
    {
        Assert.False(Factory.TryWrapSprite(null!, out var missing));
        Assert.Null(missing);

        // Anything the game handed a mod that is not a sprite - a texture, a component, a plain object - is
        // refused, so a mod that wrapped the wrong field out of the game's data is told rather than handed a
        // handle that would fail later.
        Assert.False(Factory.TryWrapSprite(new object(), out var foreign));
        Assert.Null(foreign);
        Assert.False(Factory.TryWrapSprite("not a sprite", out var text));
        Assert.Null(text);
    }

    [Fact]
    public void Unwrapping_a_pixel_set_refuses_an_object_that_is_not_one_of_the_games()
    {
        Assert.False(Factory.TryUnwrapCharacterSpriteSet(null!, out var missing, out _));
        Assert.True(missing.Main.IsEmpty);

        Assert.False(Factory.TryUnwrapCharacterSpriteSet(new object(), out var foreign, out var style));
        Assert.True(foreign.Main.IsEmpty);
        // Nothing of the refused object reaches the caller, so a mod that rebuilds a set from an empty answer
        // is refused by TryCreateCharacterSpriteSet instead of building one out of nothing.
        Assert.Null(style.IsHina);
    }
}
