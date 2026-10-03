namespace Mystia.Imgui;

// The editing state of a text control, the part of the engine's text editor a mod moves a caret with. The
// state itself stays in the engine, which is why a drawer hands out a view of it rather than a value: what a
// mod reads here is where the caret is right now, and what it writes here is where the caret goes.
/// <summary>
/// The editing state of the text control a mod is driving, read with
/// <see cref="IIMGUIDrawer.GetStateObject{T}"/>. Writing a caret position is how a mod puts the caret at the
/// end of a line it just replaced, since the name based focus API is not part of the game build.
/// </summary>
public abstract class TextInputState
{
    internal TextInputState()
    {
    }

    /// <summary>Where the caret is.</summary>
    public abstract int CursorIndex { get; set; }

    /// <summary>The other end of the selection; equal to <see cref="CursorIndex"/> when nothing is selected.</summary>
    public abstract int SelectIndex { get; set; }
}
