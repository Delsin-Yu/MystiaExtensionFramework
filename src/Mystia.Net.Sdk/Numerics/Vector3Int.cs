namespace Mystia.Numerics;

// Mirror of UnityEngine.Vector3Int for mods that must not reference the engine.
/// <summary>A three component integer vector.</summary>
/// <param name="X">The x component.</param>
/// <param name="Y">The y component.</param>
/// <param name="Z">The z component.</param>
public partial record struct Vector3Int(int X, int Y, int Z)
{
    /// <summary>The vector with all three components at zero.</summary>
    public static readonly Vector3Int Zero = new(0, 0, 0);

    public static Vector3Int operator +(Vector3Int left, Vector3Int right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static Vector3Int operator -(Vector3Int left, Vector3Int right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
}
