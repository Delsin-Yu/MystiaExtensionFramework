namespace Mystia.Numerics;

// Mirror of UnityEngine.BoundsInt for mods that must not reference the engine.
/// <summary>An axis aligned integer box anchored at its smallest corner.</summary>
/// <param name="Position">The corner with the smallest components.</param>
/// <param name="Size">The size of the box in cells.</param>
public partial record struct BoundsInt(Vector3Int Position, Vector3Int Size)
{
    /// <summary>The corner with the smallest components.</summary>
    public Vector3Int Min => Position;

    /// <summary>The corner just past the box; the box does not own it.</summary>
    public Vector3Int Max => Position + Size;
}
