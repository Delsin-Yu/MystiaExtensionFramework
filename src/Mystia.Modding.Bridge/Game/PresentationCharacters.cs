using Mystia.Assets;
using Mystia.Scenes;

namespace Mystia.Modding.Bridge;

// The character half of a scene session (see IPresentationServices): the character a mod is already working with
// is wrapped into an opaque handle, and a pixel sprite set the asset factory built is put on it. It lives in its
// own part of PresentationServices because the engine work it drives is CharacterSprites; what is left here is
// the scene scope and the refusal, exactly like the label and effect entries next to it.
internal sealed partial class PresentationServices
{
    public CharacterHandle? BindCharacter(object character)
    {
        ServiceScope.Require();
        return CharacterSprites.Bind(character);
    }

    public bool ApplyCharacterSprite(CharacterHandle character, CharacterSpriteSetHandle spriteSet, bool restart = true)
    {
        ServiceScope.Require();
        return CharacterSprites.Apply(character, spriteSet, restart);
    }
}
