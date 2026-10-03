using System.Text;

namespace Mystia.Numerics;

// Mirror of UnityEngine.Vector3 for mods that must not reference the engine. See Vector2.cs for the shape.
/// <summary>A three component float vector.</summary>
/// <param name="X">The x component.</param>
/// <param name="Y">The y component.</param>
/// <param name="Z">The z component.</param>
public partial record struct Vector3(float X, float Y, float Z)
{
    /// <summary>The vector with all three components at zero.</summary>
    public static readonly Vector3 Zero = new(0f, 0f, 0f);

    /// <summary>The vector with all three components at one.</summary>
    public static readonly Vector3 One = new(1f, 1f, 1f);

    /// <summary>The vector pointing down (0, -1, 0).</summary>
    public static readonly Vector3 Down = new(0f, -1f, 0f);

    /// <summary>The squared length of the vector; cheaper than <see cref="Magnitude"/> when only comparing.</summary>
    public float SqrMagnitude => X * X + Y * Y + Z * Z;

    /// <summary>The length of the vector.</summary>
    public float Magnitude => MathF.Sqrt(SqrMagnitude);

    /// <summary>The unit vector with the same direction; zero when the vector is too short to normalize.</summary>
    public Vector3 Normalized
    {
        get
        {
            var magnitude = Magnitude;
            return magnitude > 1e-5f ? new Vector3(X / magnitude, Y / magnitude, Z / magnitude) : Zero;
        }
    }

    public static Vector3 operator +(Vector3 left, Vector3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static Vector3 operator -(Vector3 left, Vector3 right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static Vector3 operator -(Vector3 value) => new(-value.X, -value.Y, -value.Z);

    public static Vector3 operator *(Vector3 value, float scale) => new(value.X * scale, value.Y * scale, value.Z * scale);

    public static Vector3 operator *(float scale, Vector3 value) => new(value.X * scale, value.Y * scale, value.Z * scale);

    public static Vector3 operator /(Vector3 value, float scale) => new(value.X / scale, value.Y / scale, value.Z / scale);

    /// <summary>Component-wise interpolation between <paramref name="a"/> and <paramref name="b"/>; t is clamped to [0, 1].</summary>
    public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Vector3(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t);
    }

    // The compiler's own PrintMembers would also print Normalized, which is a Vector3 printing its own
    // Normalized: ToString would recurse until the stack dies. Only the positional values are printed.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("X = ").Append(X).Append(", Y = ").Append(Y).Append(", Z = ").Append(Z);
        return true;
    }
}
