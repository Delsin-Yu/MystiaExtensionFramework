namespace Mystia.Imgui;

// The skin a mod starts its styles from: every style is a fresh copy of the skin's own, so whatever a mod
// writes stays with that mod and cannot change how another mod's panel — or the game's own IMGUI — looks.
/// <summary>
/// The skin the game draws with, as <see cref="IIMGUIDrawer.Skin"/> reports it. Read a style off it to start
/// one: each access copies the skin's current style, so a mod changes its own copy and never the skin.
/// </summary>
public abstract class SkinHandle
{
    internal SkinHandle()
    {
    }

    /// <summary>A fresh copy of the skin's label style.</summary>
    public abstract TextStyleHandle Label { get; }

    /// <summary>A fresh copy of the skin's text field style.</summary>
    public abstract TextStyleHandle TextField { get; }

    /// <summary>A fresh copy of the skin's button style.</summary>
    public abstract TextStyleHandle Button { get; }

    /// <summary>The skin's font, or <see langword="null"/> while the skin carries none.</summary>
    public abstract FontHandle? Font { get; }
}
