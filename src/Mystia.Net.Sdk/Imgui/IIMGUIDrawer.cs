using Mystia.Numerics;

namespace Mystia.Imgui;

// Drawing surface for mods that want IMGUI without touching UnityEngine directly.
// Only members that exist in the game build are exposed: the name based focus API
// (SetNextControlName / FocusControl / GetNameOfFocusedControl) is not part of that build,
// so focus is handled through KeyboardControl.
//
// Every type this interface names lives in the framework: the mirror values of Mystia.Numerics and the
// handles of Mystia.Imgui. A mod therefore holds no engine object, which also means no Unity thread
// affinity crosses this boundary — but IMGUI itself still may only be touched from the draw callback the
// drawer arrives on, so everything here is main thread only, exactly like the engine's own GUI calls.
/// <summary>
/// The IMGUI surface a mod draws with. The framework passes one drawer to
/// <see cref="IIMGUIProvider.OnGui"/>; a mod never builds one.
/// <para>
/// Positions and sizes are <see cref="Rect"/> and colours are <see cref="Color"/>, both from
/// <see cref="Mystia.Numerics"/>. Anything the engine owns — a skin, a style, a texture, a font — reaches
/// a mod as a handle declared in this namespace and is handed straight back to the drawer that produced it.
/// </para>
/// </summary>
public interface IIMGUIDrawer
{
    /// <summary>The size of the drawing surface in points.</summary>
    Vector2 ScreenSize { get; }

    /// <summary>The tint every following draw call is multiplied with; set it back when a panel is done with it.</summary>
    Color Color { get; set; }

    /// <summary>The skin the game draws with. Read a style off it to start one, nothing on the skin itself is writable.</summary>
    SkinHandle Skin { get; }

    /// <summary>
    /// A texture that is uniformly white, the equivalent of the engine's built in white texture. Drawn with
    /// <see cref="Color"/> it fills a rectangle with a flat colour, which is how a mod draws its own panels.
    /// </summary>
    TextureHandle WhiteTexture { get; }

    /// <summary>
    /// A font built from one of the operating system's own families, for a panel that needs glyphs the skin's
    /// font does not carry. The framework caches nothing: hold the handle and hand it to a style, and ask for
    /// it once — the engine builds a font per call. <see langword="null"/> when the family is not installed.
    /// </summary>
    FontHandle? CreateFontFromOsFont(string fontName, int size);

    /// <summary>The event the draw callback is currently handling, projected as a read only view.</summary>
    ImguiEvent Current { get; }

    /// <summary>
    /// The id of the control the keyboard is focused on. Assign the id of the text field a mod wants to
    /// focus, usually <see cref="LastControlId"/> right after that field was drawn.
    /// </summary>
    int KeyboardControl { get; set; }

    /// <summary>Control id created by the most recent control drawn through this drawer.</summary>
    int LastControlId { get; }

    /// <summary>Draws text with the skin's default label style.</summary>
    void Label(Rect position, string text);

    /// <summary>Draws text with <paramref name="style"/>; the style's writes are visible to this call.</summary>
    void Label(Rect position, string text, TextStyleHandle style);

    /// <summary>Draws a button and reports whether it was clicked in the event being handled.</summary>
    bool Button(Rect position, string text);

    /// <summary>Draws a button with <paramref name="style"/> and reports whether it was clicked.</summary>
    bool Button(Rect position, string text, TextStyleHandle style);

    /// <summary>Draws an editable single line text field and returns its text after the event was handled.</summary>
    string TextField(Rect position, string text);

    /// <summary>Draws an editable single line text field with <paramref name="style"/> and returns its text.</summary>
    string TextField(Rect position, string text, TextStyleHandle style);

    /// <summary>Draws a texture into <paramref name="position"/>, tinted by the current <see cref="Color"/>.</summary>
    void DrawTexture(Rect position, TextureHandle image, ImguiScaleMode scaleMode, bool alphaBlend);

    /// <summary>
    /// Starts a scroll view over <paramref name="viewRect"/> and returns the scroll position to pass back in
    /// on the next frame. Every call must be paired with <see cref="EndScrollView"/> in the same event.
    /// </summary>
    Vector2 BeginScrollView(Rect position, Vector2 scrollPosition, Rect viewRect);

    /// <summary>Ends the scroll view started by <see cref="BeginScrollView"/>.</summary>
    void EndScrollView();

    /// <summary>Starts a horizontal <c>GUILayout</c> group; every call must be paired with <see cref="EndHorizontal"/>.</summary>
    void BeginHorizontal();

    /// <summary>Ends the horizontal group started by <see cref="BeginHorizontal"/>.</summary>
    void EndHorizontal();

    /// <summary>Starts a vertical <c>GUILayout</c> group; every call must be paired with <see cref="EndVertical"/>.</summary>
    void BeginVertical();

    /// <summary>Ends the vertical group started by <see cref="BeginVertical"/>.</summary>
    void EndVertical();

    /// <summary>
    /// The framework's editing state of the control drawn with <paramref name="id"/> — the id a text field
    /// reported through <see cref="LastControlId"/> — or <see langword="null"/> while that control has no
    /// state yet. It carries the caret and selection a mod moves to put the caret at the end of the text it
    /// just changed.
    /// </summary>
    /// <typeparam name="T">The state type to read; the framework exposes <see cref="TextInputState"/>.</typeparam>
    T? GetStateObject<T>(int id) where T : TextInputState;
}
