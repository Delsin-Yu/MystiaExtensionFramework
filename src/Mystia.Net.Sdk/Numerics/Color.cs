namespace Mystia.Numerics;

// Mirror of UnityEngine.Color for mods that must not reference the engine.
/// <summary>An RGBA colour with float components.</summary>
/// <param name="R">The red component.</param>
/// <param name="G">The green component.</param>
/// <param name="B">The blue component.</param>
/// <param name="A">The alpha component; opaque when left out.</param>
public partial record struct Color(float R, float G, float B, float A = 1f)
{
    /// <summary>Opaque white.</summary>
    public static readonly Color White = new(1f, 1f, 1f, 1f);

    /// <summary>Opaque black.</summary>
    public static readonly Color Black = new(0f, 0f, 0f, 1f);

    /// <summary>Fully transparent black.</summary>
    public static readonly Color Clear = new(0f, 0f, 0f, 0f);

    /// <summary>Scales every component, alpha included.</summary>
    public static Color operator *(Color value, float scale) =>
        new(value.R * scale, value.G * scale, value.B * scale, value.A * scale);

    /// <summary>Multiplies two colours component-wise, alpha included.</summary>
    public static Color operator *(Color left, Color right) =>
        new(left.R * right.R, left.G * right.G, left.B * right.B, left.A * right.A);

    /// <summary>Component-wise interpolation between <paramref name="a"/> and <paramref name="b"/>; t is clamped to [0, 1].</summary>
    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color(
            a.R + (b.R - a.R) * t,
            a.G + (b.G - a.G) * t,
            a.B + (b.B - a.B) * t,
            a.A + (b.A - a.A) * t);
    }
}
