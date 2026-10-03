namespace Mystia.Imgui;

/// <summary>How a texture is fitted into the rectangle it is drawn in.</summary>
public enum ImguiScaleMode
{
    /// <summary>The texture is stretched to fill the whole rectangle. The mode mod panels draw in.</summary>
    StretchToFill,
}

// Opaque texture. The framework owns every texture a mod draws, so a handle cannot be built by a mod and no
// engine object crosses the boundary; the built in white texture is the one every drawer hands out, because
// a flat colour behind a panel is a white texture tinted with IIIMGUIDrawer.Color.
/// <summary>
/// A texture a mod draws with, as <see cref="IIMGUIDrawer.WhiteTexture"/> reports it. A mod hands a handle
/// back to the drawer that produced it or into a style's background, and cannot build one itself.
/// </summary>
public abstract class TextureHandle
{
    /// <summary>The white texture every drawer hands out; resolved to the engine's own white texture when drawn.</summary>
    internal static readonly TextureHandle White = new WhiteTexture();

    internal TextureHandle()
    {
    }

    private sealed class WhiteTexture : TextureHandle
    {
    }
}
