namespace Mystia.Assets;

// Opaque host object, the counterpart of SpriteHandle for the presentation surface: a label hangs on a game
// object's transform, and a mod holds that transform only through this handle. The engine object stays behind
// the bridge, which is why nothing here names one and why the handle cannot be built by a mod.
/// <summary>
/// An opaque host object a floating label hangs on. A mod wraps the game object, component or transform it is
/// working with through <c>IPresentationServices.Bind</c> and hands the handle to the label entries; it cannot
/// build one itself.
/// </summary>
public abstract class TransformHandle
{
    internal TransformHandle()
    {
    }
}
