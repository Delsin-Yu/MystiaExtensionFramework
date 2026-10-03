using System.Text;

namespace Mystia.Numerics;

// Mirror of UnityEngine.Vector2 for mods that must not reference the engine. Only the members mods actually
// use are mirrored here: the engine-facing constructors and ToUnity live in GameApi/NumericsUnity.cs, which
// is compiled only when the game interop is present.
/// <summary>A two component float vector.</summary>
/// <param name="X">The horizontal component.</param>
/// <param name="Y">The vertical component.</param>
public partial record struct Vector2(float X, float Y)
{
    /// <summary>The vector with both components at zero.</summary>
    public static readonly Vector2 Zero = new(0f, 0f);

    /// <summary>The vector with both components at one.</summary>
    public static readonly Vector2 One = new(1f, 1f);

    /// <summary>The squared length of the vector; cheaper than <see cref="Magnitude"/> when only comparing.</summary>
    public float SqrMagnitude => X * X + Y * Y;

    /// <summary>The length of the vector.</summary>
    public float Magnitude => MathF.Sqrt(SqrMagnitude);

    /// <summary>The unit vector with the same direction; zero when the vector is too short to normalize.</summary>
    public Vector2 Normalized
    {
        get
        {
            var magnitude = Magnitude;
            return magnitude > 1e-5f ? new Vector2(X / magnitude, Y / magnitude) : Zero;
        }
    }

    public static Vector2 operator +(Vector2 left, Vector2 right) => new(left.X + right.X, left.Y + right.Y);

    public static Vector2 operator -(Vector2 left, Vector2 right) => new(left.X - right.X, left.Y - right.Y);

    public static Vector2 operator -(Vector2 value) => new(-value.X, -value.Y);

    public static Vector2 operator *(Vector2 value, float scale) => new(value.X * scale, value.Y * scale);

    public static Vector2 operator *(float scale, Vector2 value) => new(value.X * scale, value.Y * scale);

    public static Vector2 operator /(Vector2 value, float scale) => new(value.X / scale, value.Y / scale);

    /// <summary>Component-wise interpolation between <paramref name="a"/> and <paramref name="b"/>; t is clamped to [0, 1].</summary>
    public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Vector2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    // The compiler's own PrintMembers prints every public property, derived ones included, and printing
    // Normalized means printing a Vector2 that prints its own Normalized: ToString would recurse until the
    // stack dies. Only the positional values are printed, which is also what the engine's own ToString says.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("X = ").Append(X).Append(", Y = ").Append(Y);
        return true;
    }

    /// <summary>Widens to <see cref="Vector3"/> with z at zero.</summary>
    public static implicit operator Vector3(Vector2 value) => new(value.X, value.Y, 0f);

    /// <summary>Narrows to the x and y of <see cref="Vector3"/>.</summary>
    public static implicit operator Vector2(Vector3 value) => new(value.X, value.Y);
}
