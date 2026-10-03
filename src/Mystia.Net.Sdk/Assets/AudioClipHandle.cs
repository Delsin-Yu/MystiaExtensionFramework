namespace Mystia.Assets;

// Opaque audio clip, the counterpart of SpriteHandle: building the clip needs the engine, holding and filing
// it does not.
/// <summary>
/// An audio clip the framework built from decoded samples (<c>IAssetFactory.TryCreateAudioClip</c>). It is
/// filed into the asset pipeline with <see cref="IAssetLocator.TryRegisterAudioClip"/>; a mod cannot build
/// one itself.
/// </summary>
public abstract class AudioClipHandle
{
    internal AudioClipHandle()
    {
    }
}
