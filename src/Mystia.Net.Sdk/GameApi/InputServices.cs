namespace Mystia.Scenes;

/// <summary>
/// A keyboard key a mod polls as a global hotkey.
/// <para>
/// Every member is named exactly like the engine's own <c>KeyCode</c> member it stands for, because a mod
/// stores a hotkey in its own configuration by that name: renaming a member would silently invalidate the
/// keys a player already saved. Only the keys a mod actually polls are listed; a member is added as a need
/// for it appears.
/// </para>
/// </summary>
public enum MystiaKey
{
    /// <summary>No key; the value of a hotkey that is not bound.</summary>
    None,

    /// <summary>The right shift key.</summary>
    RightShift,

    /// <summary>The backslash key.</summary>
    Backslash,

    /// <summary>The forward slash key.</summary>
    Slash,

    /// <summary>The T key.</summary>
    T,

    /// <summary>The first function key.</summary>
    F1,

    /// <summary>The second function key.</summary>
    F2,

    /// <summary>The third function key.</summary>
    F3,

    /// <summary>The main return key.</summary>
    Return,

    /// <summary>The keypad's enter key.</summary>
    KeypadEnter,

    /// <summary>The escape key.</summary>
    Escape,
}

/// <summary>
/// The keyboard, as a mod polls it for its own global hotkeys. Every member reports the input state of the
/// frame being processed, so this is main thread only — the engine's input state is.
/// <para>
/// The IMGUI event a panel handles (see <c>Mystia.Imgui.ImguiEvent</c>) is not a replacement: it only exists
/// inside an <c>OnGUI</c> callback and only reports the keys a text field reacts to, neither of which holds
/// for a hotkey read from a loop.
/// </para>
/// <para>
/// A key release is not reported: the engine metadata of the build the framework targets carries no key up
/// query, so a hotkey is read from the down edge or from the held state only.
/// </para>
/// </summary>
public interface IInputServices
{
    /// <summary>Whether <paramref name="key"/> went down during the frame being processed.</summary>
    bool IsKeyDown(MystiaKey key);

    /// <summary>Whether <paramref name="key"/> is held during the frame being processed.</summary>
    bool IsKeyHeld(MystiaKey key);
}
