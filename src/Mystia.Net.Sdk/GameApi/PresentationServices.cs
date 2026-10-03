using UnityEngine;

namespace Mystia.Scenes;

/// <summary>
/// The presentation members of a scene session: the camera, the effect and audio layers, and the world
/// positions the running scene owns.
///
/// They act on the scene that is running right now, so they are only valid inside the <c>Setup</c>,
/// <c>Update</c> and <c>Shutdown</c> of the scene loop they were handed to; a scene change invalidates them
/// and every call made outside that window throws. A global loop never sees this interface.
/// </summary>
public interface IPresentationServices
{
    /// <summary>Shakes the camera for the given duration, with the given strength and frequency.</summary>
    void ShakeCamera(float duration, float strength, float frequency) => throw new NotSupportedException();

    /// <summary>
    /// Plays the effect asset at a path declared like every other mod resource, at a world position, and
    /// returns a handle for stopping it early.
    /// </summary>
    IVfxHandle PlayVfx(string assetPath, Vector3 position) => throw new NotSupportedException();

    /// <summary>Plays the audio asset at a path declared like every other mod resource.</summary>
    void PlayAudio(string assetPath) => throw new NotSupportedException();

    /// <summary>World position of the player character.</summary>
    Vector3 PlayerPosition => throw new NotSupportedException();

    /// <summary>World position of the table of a desk.</summary>
    Vector3 TablePosition(int deskCode) => throw new NotSupportedException();
}
