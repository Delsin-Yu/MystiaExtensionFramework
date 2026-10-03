using Il2CppInterop.Runtime.InteropTypes;

using UnityEngine;

namespace Mystia;

[AutoWire]
public interface IIMGUIProvider
{
    void OnGui(IIMGUIDrawer drawer);
}

// Drawing surface for mods that want IMGUI without touching UnityEngine directly.
// Only members that exist in the game build are exposed: the name based focus API
// (SetNextControlName / FocusControl / GetNameOfFocusedControl) is not part of that build,
// so focus is handled through KeyboardControl.
public interface IIMGUIDrawer
{
    Vector2 ScreenSize { get; }

    Color Color { get; set; }

    GUISkin Skin { get; }

    Event Current { get; }

    int KeyboardControl { get; set; }

    /// <summary>Control id created by the most recent control drawn through this drawer.</summary>
    int LastControlId { get; }

    void Label(Rect position, string text);

    void Label(Rect position, string text, GUIStyle style);

    bool Button(Rect position, string text);

    bool Button(Rect position, string text, GUIStyle style);

    string TextField(Rect position, string text);

    string TextField(Rect position, string text, GUIStyle style);

    void DrawTexture(Rect position, Texture image, ScaleMode scaleMode, bool alphaBlend);

    Vector2 BeginScrollView(Rect position, Vector2 scrollPosition, Rect viewRect);

    void EndScrollView();

    void BeginHorizontal();

    void EndHorizontal();

    void BeginVertical();

    void EndVertical();

    T GetStateObject<T>(int id) where T : Il2CppObjectBase;
}
