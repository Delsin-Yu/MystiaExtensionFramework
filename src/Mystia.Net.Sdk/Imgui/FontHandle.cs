namespace Mystia.Imgui;

// Opaque font. A mod receives fonts from the framework — today the skin's own, through SkinHandle.Font and
// TextStyleHandle.Font — compares them and hands them back to a style. There is no public way to build one,
// so a font a mod names is always a font the framework already owns.
/// <summary>
/// A font a style draws with, reached through <see cref="SkinHandle.Font"/> and written to
/// <see cref="TextStyleHandle.Font"/>. Handing a style <see langword="null"/> asks the engine for its
/// default font.
/// </summary>
public abstract class FontHandle
{
    internal FontHandle()
    {
    }
}
