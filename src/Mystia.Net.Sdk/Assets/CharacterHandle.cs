namespace Mystia.Assets;

// Opaque character, the counterpart of TransformHandle for the characters of the running scene: the game's own
// character unit stays behind the bridge, so a mod that wants to put art on a character holds the character
// only through this handle and never names CharacterControllerUnit to do it.
/// <summary>
/// An opaque character of the running scene. A mod wraps the character it is already working with — the unit
/// itself, its game object or any component on it — through <c>IPresentationServices.BindCharacter</c>, and
/// hands the handle to <c>IPresentationServices.ApplyCharacterSprite</c>; it cannot build one itself.
/// </summary>
public abstract class CharacterHandle
{
    internal CharacterHandle()
    {
    }
}
