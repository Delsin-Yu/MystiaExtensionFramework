using System.Text;

namespace Mystia.Numerics;

// Mirror of UnityEngine.Rect for mods that must not reference the engine.
/// <summary>An axis aligned rectangle anchored at its top left corner.</summary>
/// <param name="X">The x of the left edge.</param>
/// <param name="Y">The y of the top edge.</param>
/// <param name="Width">The width of the rectangle.</param>
/// <param name="Height">The height of the rectangle.</param>
public partial record struct Rect(float X, float Y, float Width, float Height)
{
    /// <summary>The right edge; the rectangle does not own it.</summary>
    public float XMax => X + Width;

    /// <summary>The bottom edge; the rectangle does not own it.</summary>
    public float YMax => Y + Height;

    /// <summary>The top left corner.</summary>
    public Vector2 Position => new(X, Y);

    /// <summary>The width and height as a vector.</summary>
    public Vector2 Size => new(Width, Height);

    /// <summary>Whether the point lies inside the rectangle.</summary>
    public bool Contains(Vector2 point) => Contains(point.X, point.Y);

    // Left and top edges are inside, right and bottom edges are outside: this is the engine's own rule and
    // the one mods depend on when they test a cursor position against a panel.
    /// <summary>Whether the point lies inside the rectangle; the left and top edges count, the right and bottom do not.</summary>
    public bool Contains(float x, float y) => x >= X && x < XMax && y >= Y && y < YMax;

    // The compiler's own PrintMembers would print Position and Size as well, and a Vector2 prints its own
    // Normalized: ToString would recurse until the stack dies. Only the positional values are printed.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("X = ").Append(X).Append(", Y = ").Append(Y)
            .Append(", Width = ").Append(Width).Append(", Height = ").Append(Height);
        return true;
    }
}
