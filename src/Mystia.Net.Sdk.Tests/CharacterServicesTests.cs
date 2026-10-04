using Mystia.Modding.Bridge;
using Mystia.Numerics;
using Mystia.Scenes;
using Xunit;

namespace Mystia.Tests;

/// <summary>
/// The character operations and the two extra common capabilities the framework added for them: what a mod may
/// ask of the game's own characters from a console command or a message handler, and what the framework refuses
/// before it touches the engine. The refusals are the testable half of the contract — a refused call answers
/// synchronously, with the documented value, and never reaches the engine — so every case below is one a mod
/// (or the host) has to keep working.
/// </summary>
// BridgeInstaller.Registry / CommonServices are process wide statics, so this class must not run in parallel
// with the other suites that bind a registry (the xunit default parallelises test classes).
[Collection("Scene loop services")]
public sealed class CharacterServicesTests
{
    /// <summary>
    /// The character operations are part of the capabilities a mod uses at any time, i.e. they are there for a
    /// global loop — where a console command runs — and not only inside a scene loop's service window.
    /// </summary>
    [Fact]
    public void The_character_operations_are_always_available()
    {
        Assert.Same(CharacterServices.Shared, CommonServices.Shared.Characters);
        Assert.Same(CommonServices.Shared.Characters, GlobalServices.Shared.Common.Characters);
    }

    /// <summary>
    /// Creating a character needs a label and a pixel set, and the speed has to be a number the game can move
    /// with: every one of these is refused before the engine is asked for anything.
    /// </summary>
    [Fact]
    public void A_character_is_created_only_from_a_label_a_pixel_set_and_a_finite_speed()
    {
        var characters = CharacterServices.Shared;

        // No label: the label is the key the game's own collection, lookups and teardown use.
        Assert.Null(characters.CreateCharacter(new CharacterCreateSpec(string.Empty, null, 1f)));
        // No skin: a character without a set has no animator.
        Assert.Null(characters.CreateCharacter(new CharacterCreateSpec("Peer", null, 1f)));
        // A skin that is not a pixel set of the game's nor one the framework built.
        Assert.Null(characters.CreateCharacter(new CharacterCreateSpec("Peer", new object(), 1f)));
        // A speed the game cannot move with.
        Assert.Null(characters.CreateCharacter(new CharacterCreateSpec("Peer", null, float.NaN)));
        Assert.Null(characters.CreateCharacter(new CharacterCreateSpec("Peer", null, float.PositiveInfinity)));
    }

    /// <summary>
    /// The spec keeps the values the game's own spawn uses when a caller has no opinion: the origin, and no
    /// collider.
    /// </summary>
    [Fact]
    public void A_create_spec_defaults_to_the_games_own_spawn()
    {
        var spec = new CharacterCreateSpec("Peer", null, 2f);

        Assert.Equal(Vector2.Zero, spec.Position);
        Assert.False(spec.HasCollider);
        Assert.Equal("Peer", spec.Label);
        Assert.Equal(2f, spec.MoveSpeedMultiplier);
    }

    /// <summary>
    /// A handle that does not name a character of this framework's — a null one, or one whose character is gone
    /// — is refused by both the removal and the height blending, so neither can reach an engine object that is no
    /// longer there.
    /// </summary>
    [Fact]
    public void A_handle_that_names_no_character_is_refused()
    {
        var characters = CharacterServices.Shared;
        var empty = new UnityCharacterHandle(null!);

        Assert.False(characters.DestroyCharacter(null!));
        Assert.False(characters.DestroyCharacter(empty));
        Assert.False(characters.SetCharacterHeightBlending(null!));
        Assert.False(characters.SetCharacterHeightBlending(empty));
    }

    /// <summary>
    /// A walk is refused over an empty label or a speed the game cannot walk with, and as well while the running
    /// scene files no character under the label. This host runs no scene, so nothing here reaches the engine.
    /// </summary>
    [Fact]
    public void A_walk_needs_a_label_and_a_positive_speed()
    {
        var characters = CharacterServices.Shared;
        var here = new Vector2(1f, 2f);

        Assert.Null(SceneLoopHost.Active);
        Assert.False(characters.WalkCharacter(string.Empty, here));
        Assert.False(characters.WalkCharacter("Cirno", here, 0f));
        Assert.False(characters.WalkCharacter("Cirno", here, float.NaN));
        Assert.False(characters.WalkCharacter("Cirno", here, -1f));
    }

}
