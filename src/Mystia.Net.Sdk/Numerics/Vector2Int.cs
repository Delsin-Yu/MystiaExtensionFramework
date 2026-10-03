namespace Mystia.Numerics;

// Mirror of UnityEngine.Vector2Int for mods that must not reference the engine.
/// <summary>A two component integer vector.</summary>
/// <param name="X">The horizontal component.</param>
/// <param name="Y">The vertical component.</param>
public partial record struct Vector2Int(int X, int Y)
{
    /// <summary>The vector with both components at zero.</summary>
    public static readonly Vector2Int Zero = new(0, 0);

    public static Vector2Int operator +(Vector2Int left, Vector2Int right) => new(left.X + right.X, left.Y + right.Y);

    public static Vector2Int operator -(Vector2Int left, Vector2Int right) => new(left.X - right.X, left.Y - right.Y);
}
