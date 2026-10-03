using Mystia.Imgui;

namespace Mystia;

// A mod that wants IMGUI implements this and the framework draws it: the drawer it receives is the whole
// engine free surface of Mystia.Imgui, so the mod never names UnityEngine itself.
/// <summary>
/// A mod's IMGUI entry point. The framework calls <see cref="OnGui"/> once for every IMGUI event of every
/// frame while the mod's screens are up, on the main thread, and hands it the drawer to draw with.
/// </summary>
[AutoWire]
public interface IIMGUIProvider
{
    /// <summary>
    /// Draws the mod's panel for the event being handled. The call may only touch the drawer it is given and
    /// only for the length of the call; the drawer outlives it, so a mod may keep it but not the rectangle
    /// state of the pass it arrived in.
    /// </summary>
    void OnGui(IIMGUIDrawer drawer);
}
