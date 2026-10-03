namespace Mystia.Assets;

// Opaque sprite. Until now a mod cut its sprites out of a texture itself; here it asks the factory to cut
// one and only ever holds the result, so no engine sprite ever crosses the boundary and a sprite a mod names
// is always one the framework built.
/// <summary>
/// A sprite the framework built from a texture and a rectangle (<c>IAssetFactory.TryCreateSprite</c>). It is
/// filed into the asset pipeline with <see cref="IAssetLocator.TryRegisterSprite"/> and handed back to the
/// framework for anything else; a mod cannot build one itself.
/// </summary>
public abstract class SpriteHandle
{
    internal SpriteHandle()
    {
    }
}
