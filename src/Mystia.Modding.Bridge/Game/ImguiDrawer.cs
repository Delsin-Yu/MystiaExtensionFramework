using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace Mystia.Modding.Bridge;

internal sealed class ImguiDrawer : IIMGUIDrawer
{
    public Vector2 ScreenSize => new(Screen.width, Screen.height);

    public Color Color
    {
        get => GUI.color;
        set => GUI.color = value;
    }

    public GUISkin Skin => GUI.skin;

    public Event Current => Event.current;

    public int KeyboardControl
    {
        get => GUIUtility.keyboardControl;
        set => GUIUtility.keyboardControl = value;
    }

    private int _lastControlId;

    public int LastControlId => _lastControlId;

    public void Label(Rect position, string text) => GUI.Label(position, text);

    public bool Button(Rect position, string text)
    {
        _lastControlId = GUIUtility.GetControlID(FocusType.Passive, position);
        return GUI.Button(position, text);
    }

    public void Label(Rect position, string text, GUIStyle style) => GUI.Label(position, new GUIContent(text), style);

    public bool Button(Rect position, string text, GUIStyle style) => GUI.Button(position, new GUIContent(text), style);

    // The generated interop carries DoTextField but not GUI.TextField itself, so this repeats
    // the wrapper's body: TextField(position, text) is exactly this call plus "return t.text".
    public string TextField(Rect position, string text)
    {
        var content = new GUIContent(text);
        _lastControlId = GUIUtility.GetControlID(FocusType.Keyboard, position);
        GUI.DoTextField(position, _lastControlId, content, false, -1, GUI.skin.textField);
        return content.text;
    }

    public string TextField(Rect position, string text, GUIStyle style)
    {
        var content = new GUIContent(text);
        _lastControlId = GUIUtility.GetControlID(FocusType.Keyboard, position);
        GUI.DoTextField(position, _lastControlId, content, false, -1, style);
        return content.text;
    }

    public void DrawTexture(Rect position, Texture image, ScaleMode scaleMode, bool alphaBlend) =>
        GUI.DrawTexture(position, image, scaleMode, alphaBlend);

    // The generated interop only carries the eight argument BeginScrollView, so this passes the
    // values the three argument overload of the engine passes.
    public Vector2 BeginScrollView(Rect position, Vector2 scrollPosition, Rect viewRect) =>
        GUI.BeginScrollView(
            position,
            scrollPosition,
            viewRect,
            false,
            false,
            GUI.skin.horizontalScrollbar,
            GUI.skin.verticalScrollbar,
            GUI.skin.scrollView);

    public void EndScrollView() => GUI.EndScrollView(true);

    public void BeginHorizontal() => GUILayout.BeginHorizontal();

    public void EndHorizontal() => GUILayout.EndHorizontal();

    public void BeginVertical() => GUILayout.BeginVertical();

    public void EndVertical() => GUILayout.EndVertical();

    public T GetStateObject<T>(int id) where T : Il2CppObjectBase => GUIUtility.GetStateObject(Il2CppType.Of<T>(), id).Cast<T>();
}
