// This file is compiled only when the game interop is present (see the GameApi condition in the csproj), so
// it is the one place that may name UnityEngine. The mirror types in Numerics/*.cs stay engine free and are
// declared partial here: the bridge, which does live next to the engine, constructs a mirror value from an
// engine value and calls ToUnity() to hand one back.

namespace Mystia.Numerics;

public partial record struct Vector2
{
    internal Vector2(UnityEngine.Vector2 value) : this(value.x, value.y)
    {
    }

    internal UnityEngine.Vector2 ToUnity() => new(X, Y);
}

public partial record struct Vector3
{
    internal Vector3(UnityEngine.Vector3 value) : this(value.x, value.y, value.z)
    {
    }

    internal UnityEngine.Vector3 ToUnity() => new(X, Y, Z);
}

public partial record struct Vector2Int
{
    internal Vector2Int(UnityEngine.Vector2Int value) : this(value.x, value.y)
    {
    }

    internal UnityEngine.Vector2Int ToUnity() => new(X, Y);
}

public partial record struct Vector3Int
{
    internal Vector3Int(UnityEngine.Vector3Int value) : this(value.x, value.y, value.z)
    {
    }

    internal UnityEngine.Vector3Int ToUnity() => new(X, Y, Z);
}

public partial record struct Vector4
{
    internal Vector4(UnityEngine.Vector4 value) : this(value.x, value.y, value.z, value.w)
    {
    }

    internal UnityEngine.Vector4 ToUnity() => new(X, Y, Z, W);
}

public partial record struct Color
{
    internal Color(UnityEngine.Color value) : this(value.r, value.g, value.b, value.a)
    {
    }

    internal UnityEngine.Color ToUnity() => new(R, G, B, A);
}

public partial record struct Rect
{
    internal Rect(UnityEngine.Rect value) : this(value.x, value.y, value.width, value.height)
    {
    }

    internal UnityEngine.Rect ToUnity() => new(X, Y, Width, Height);
}

public partial record struct Bounds
{
    internal Bounds(UnityEngine.Bounds value) : this(new Vector3(value.center), new Vector3(value.size))
    {
    }

    internal UnityEngine.Bounds ToUnity() => new(Center.ToUnity(), Size.ToUnity());
}

public partial record struct BoundsInt
{
    internal BoundsInt(UnityEngine.BoundsInt value) : this(new Vector3Int(value.position), new Vector3Int(value.size))
    {
    }

    internal UnityEngine.BoundsInt ToUnity() => new(Position.ToUnity(), Size.ToUnity());
}
