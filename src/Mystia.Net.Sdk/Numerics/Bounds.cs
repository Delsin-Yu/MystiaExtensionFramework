using System.Text;

namespace Mystia.Numerics;

// Mirror of UnityEngine.Bounds for mods that must not reference the engine.
/// <summary>An axis aligned box described by its centre and its size.</summary>
/// <param name="Center">The centre of the box.</param>
/// <param name="Size">The full size of the box, not the half size.</param>
public partial record struct Bounds(Vector3 Center, Vector3 Size)
{
    /// <summary>Half the size, the distance from the centre to each face.</summary>
    public Vector3 Extents => Size * 0.5f;

    /// <summary>The corner with the smallest components.</summary>
    public Vector3 Min => Center - Extents;

    /// <summary>The corner with the largest components.</summary>
    public Vector3 Max => Center + Extents;

    // The compiler's own PrintMembers would also print Extents, Min and Max, which are Vector3 values printing
    // their own Normalized: ToString would recurse until the stack dies. Only the positional values are printed.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Center = ").Append(Center).Append(", Size = ").Append(Size);
        return true;
    }
}
