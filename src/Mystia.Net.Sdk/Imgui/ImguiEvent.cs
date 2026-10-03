using Mystia.Numerics;

namespace Mystia.Imgui;

/// <summary>The kind of one IMGUI event, restricted to the kinds mod panels react to.</summary>
public enum ImguiEventKind
{
    /// <summary>The layout pass; run control count sensitive work here so the repaint pass matches it.</summary>
    Layout,

    /// <summary>A key went down.</summary>
    KeyDown,

    /// <summary>A mouse button went down.</summary>
    MouseDown,

    /// <summary>The mouse moved while a button was held.</summary>
    MouseDrag,

    /// <summary>A mouse button came up.</summary>
    MouseUp,

    /// <summary>The repaint pass; draw here.</summary>
    Repaint,

    /// <summary>
    /// An engine event none of the kinds above describes (a mouse move, a scroll wheel, ...). It is never
    /// reported as <see cref="Layout"/> or <see cref="Repaint"/>, so work a mod does only in those passes
    /// cannot run on the wrong event.
    /// </summary>
    Other,
}

/// <summary>The keys an IMGUI event reports, restricted to the keys mod panels react to.</summary>
public enum ImguiKey
{
    /// <summary>No key; the value of an event that is not a key event.</summary>
    None,

    /// <summary>Escape.</summary>
    Escape,

    /// <summary>Tab.</summary>
    Tab,

    /// <summary>Return.</summary>
    Return,

    /// <summary>The keypad's enter key.</summary>
    KeypadEnter,

    /// <summary>The up arrow.</summary>
    UpArrow,

    /// <summary>The down arrow.</summary>
    DownArrow,
}

// The projection carries only what a mod reads off the event it handles. It is a value, so a mod may hold it
// for the length of the callback it arrived in; Use() reaches back into the engine event the framework
// projected, which is why the projection is built again for every event rather than cached.
/// <summary>
/// A read only projection of the IMGUI event being handled, as <see cref="IIMGUIDrawer.Current"/> reports it.
/// A mod reads it and, when it has dealt with the event, consumes it with <see cref="Use"/> so the controls
/// drawn later in the same pass do not handle it again.
/// </summary>
public readonly struct ImguiEvent
{
    private readonly Action? _consume;

    internal ImguiEvent(
        ImguiEventKind kind,
        ImguiKey key,
        bool shift,
        char character,
        Vector2 mousePosition,
        Action? consume = null)
    {
        Kind = kind;
        Key = key;
        Shift = shift;
        Character = character;
        MousePosition = mousePosition;
        _consume = consume;
    }

    /// <summary>What the event is. A mod reacts to the kinds it knows and ignores the rest.</summary>
    public ImguiEventKind Kind { get; }

    /// <summary>The key the event reports, <see cref="ImguiKey.None"/> when it is not a key event.</summary>
    public ImguiKey Key { get; }

    /// <summary>Whether shift was held.</summary>
    public bool Shift { get; }

    /// <summary>The character a key event produced; <c>'\0'</c> when the key produces no character.</summary>
    public char Character { get; }

    /// <summary>Where the mouse was, in the same points as the rectangles passed to the drawer.</summary>
    public Vector2 MousePosition { get; }

    /// <summary>
    /// Consumes the event: the engine marks it used, so the controls drawn after this one do not see it and
    /// the click or key stroke a mod just handled cannot also fire something else.
    /// <para>
    /// Only a <see cref="ImguiEventKind.KeyDown"/>, <see cref="ImguiEventKind.MouseDown"/>,
    /// <see cref="ImguiEventKind.MouseDrag"/> or <see cref="ImguiEventKind.MouseUp"/> event can be consumed;
    /// for every other kind — in particular the layout and repaint passes — this does nothing, because the
    /// engine does not let those be consumed and a mod should not have to guard the call. Projections a mod
    /// builds itself, in a test for instance, simply have nothing to consume.
    /// </para>
    /// </summary>
    public void Use()
    {
        if (Kind is ImguiEventKind.KeyDown or ImguiEventKind.MouseDown or ImguiEventKind.MouseDrag or ImguiEventKind.MouseUp)
            _consume?.Invoke();
    }
}
