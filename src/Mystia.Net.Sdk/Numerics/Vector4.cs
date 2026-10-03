namespace Mystia.Numerics;

// Mirror of UnityEngine.Vector4 for mods that must not reference the engine.
/// <summary>A four component float vector.</summary>
/// <param name="X">The x component.</param>
/// <param name="Y">The y component.</param>
/// <param name="Z">The z component.</param>
/// <param name="W">The w component.</param>
public partial record struct Vector4(float X, float Y, float Z, float W)
{
    /// <summary>The vector with all four components at zero.</summary>
    public static readonly Vector4 Zero = new(0f, 0f, 0f, 0f);

    /// <summary>The vector with all four components at one.</summary>
    public static readonly Vector4 One = new(1f, 1f, 1f, 1f);
}
