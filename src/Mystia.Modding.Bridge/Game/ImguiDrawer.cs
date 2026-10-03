using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Mystia.Imgui;
using UnityEngine;
using MirrorColor = Mystia.Numerics.Color;
using MirrorRect = Mystia.Numerics.Rect;
using MirrorVector2 = Mystia.Numerics.Vector2;

namespace Mystia.Modding.Bridge;

// The drawer mods draw with. The methods below are the engine side of Mystia.Imgui.IIMGUIDrawer: every
// parameter and return value is a mirror value or a handle, and the engine calls they stand for happen in
// here, on the main thread, while the game draws.
internal sealed class ImguiDrawer : IIMGUIDrawer
{
    public MirrorVector2 ScreenSize => new(Screen.width, Screen.height);

    public MirrorColor Color
    {
        get => new(GUI.color);
        set => GUI.color = value.ToUnity();
    }

    // The skin is read per access rather than cached, so a mod that switches skin mid session draws with the
    // one the engine currently has; only the wrapper is built here, the styles it hands out are built on use.
    public SkinHandle Skin => new UnitySkin(GUI.skin);

    // The engine's built in white texture, as a handle the framework manages; the mod tints it with Color to
    // fill a rectangle, so the mod never has to make a texture of its own.
    public TextureHandle WhiteTexture => TextureHandle.White;

    // Font.CreateDynamicFontFromOSFont builds a font per call and keeps it alive through the handle a mod
    // holds; a family that is not installed comes back null, which a mod reads as "keep the skin's font".
    public FontHandle? CreateFontFromOsFont(string fontName, int size)
    {
        if (string.IsNullOrWhiteSpace(fontName) || size <= 0)
            return null;

        var font = Font.CreateDynamicFontFromOSFont(fontName, size);
        return font == null ? null : new UnityFontHandle(font);
    }

    public ImguiEvent Current => ImguiMirror.Project(Event.current);

    public int KeyboardControl
    {
        get => GUIUtility.keyboardControl;
        set => GUIUtility.keyboardControl = value;
    }

    private int _lastControlId;

    public int LastControlId => _lastControlId;

    public void Label(MirrorRect position, string text) => GUI.Label(position.ToUnity(), text);

    public void Label(MirrorRect position, string text, TextStyleHandle style) =>
        GUI.Label(position.ToUnity(), new GUIContent(text), ImguiMirror.Style(style));

    public bool Button(MirrorRect position, string text)
    {
        _lastControlId = GUIUtility.GetControlID(FocusType.Passive, position.ToUnity());
        return GUI.Button(position.ToUnity(), text);
    }

    public bool Button(MirrorRect position, string text, TextStyleHandle style)
    {
        var rect = position.ToUnity();
        _lastControlId = GUIUtility.GetControlID(FocusType.Passive, rect);
        return GUI.Button(rect, new GUIContent(text), ImguiMirror.Style(style));
    }

    // The generated interop carries DoTextField but not GUI.TextField itself, so this repeats
    // the wrapper's body: TextField(position, text) is exactly this call plus "return t.text".
    public string TextField(MirrorRect position, string text)
    {
        var content = new GUIContent(text);
        _lastControlId = GUIUtility.GetControlID(FocusType.Keyboard, position.ToUnity());
        GUI.DoTextField(position.ToUnity(), _lastControlId, content, false, -1, GUI.skin.textField);
        return content.text;
    }

    public string TextField(MirrorRect position, string text, TextStyleHandle style)
    {
        var content = new GUIContent(text);
        var rect = position.ToUnity();
        _lastControlId = GUIUtility.GetControlID(FocusType.Keyboard, rect);
        GUI.DoTextField(rect, _lastControlId, content, false, -1, ImguiMirror.Style(style));
        return content.text;
    }

    public void DrawTexture(MirrorRect position, TextureHandle image, ImguiScaleMode scaleMode, bool alphaBlend) =>
        GUI.DrawTexture(position.ToUnity(), ImguiMirror.Resolve(image), ImguiMirror.Resolve(scaleMode), alphaBlend);

    // The generated interop only carries the eight argument BeginScrollView, so this passes the
    // values the three argument overload of the engine passes.
    public MirrorVector2 BeginScrollView(MirrorRect position, MirrorVector2 scrollPosition, MirrorRect viewRect) =>
        new(GUI.BeginScrollView(
            position.ToUnity(),
            scrollPosition.ToUnity(),
            viewRect.ToUnity(),
            false,
            false,
            GUI.skin.horizontalScrollbar,
            GUI.skin.verticalScrollbar,
            GUI.skin.scrollView));

    public void EndScrollView() => GUI.EndScrollView(true);

    public void BeginHorizontal() => GUILayout.BeginHorizontal();

    public void EndHorizontal() => GUILayout.EndHorizontal();

    public void BeginVertical() => GUILayout.BeginVertical();

    public void EndVertical() => GUILayout.EndVertical();

    // The mod names the state it wants as TextInputState; the engine object behind it is the text editor of
    // the field, and the handle reaches that object, so a caret a mod writes lands in the control the engine
    // draws with.
    public T? GetStateObject<T>(int id) where T : TextInputState
    {
        var state = GUIUtility.GetStateObject(Il2CppType.Of<TextEditor>(), id);
        if (state == null)
            return null;

        return new UnityTextInputState(state.Cast<TextEditor>()) as T;
    }
}
