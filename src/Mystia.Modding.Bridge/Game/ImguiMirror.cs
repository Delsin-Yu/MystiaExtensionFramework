using Mystia.Imgui;
using UnityEngine;
using MirrorColor = Mystia.Numerics.Color;
using MirrorVector2 = Mystia.Numerics.Vector2;

namespace Mystia.Modding.Bridge;

// The engine side of the IMGUI facade. The handles a mod holds are declared in Mystia.Imgui and carry no
// engine reference at all; this file is the only place that turns one back into the engine object it stands
// for, which is why it lives under Game/ with the rest of the interop bound code.
//
// A handle that is written to keeps the write on the engine object the moment that object is next used: an
// IMGUI style is read once, the moment a mod takes it off the skin, and pushed back into the style the draw
// call uses whenever the mod changed something since (TextStyleHandle.Version). So the next draw call after a
// write sees the new value and the drawing order inside one event is the order the mod's code implies —
// nothing is deferred a frame, and an unwritten value is never touched at all, so a style restyled in one
// place keeps the skin's own look everywhere else.
//
// What the handle can and cannot be told back matters here: this game build's GUIStyle only reports the font,
// word wrap, padding and margin back. The size, alignment, font style, rich text and the state colours and
// backgrounds are write only — the engine's own StyleState has no getters — so the framework reads the ones it
// can into the handle when a mod takes a style off the skin and leaves the rest to the engine style.
internal static class ImguiMirror
{
    // ── events ────────────────────────────────────────────────────────────────
    internal static ImguiEvent Project(Event? source)
    {
        if (source == null)
            return new ImguiEvent(ImguiEventKind.Other, ImguiKey.None, false, '\0', MirrorVector2.Zero);

        // The engine's own Use is the consumer, so consuming the projection consumes the event the mod is
        // handling and not whatever event happens to be current when the call is made.
        return new ImguiEvent(
            Kind(source.type),
            Key(source.keyCode),
            source.shift,
            source.character,
            new MirrorVector2(source.mousePosition),
            source.Use);
    }

    private static ImguiEventKind Kind(EventType type) => type switch
    {
        EventType.Layout => ImguiEventKind.Layout,
        EventType.KeyDown => ImguiEventKind.KeyDown,
        EventType.MouseDown => ImguiEventKind.MouseDown,
        EventType.MouseDrag => ImguiEventKind.MouseDrag,
        EventType.MouseUp => ImguiEventKind.MouseUp,
        EventType.Repaint => ImguiEventKind.Repaint,
        _ => ImguiEventKind.Other,
    };

    private static ImguiKey Key(KeyCode key) => key switch
    {
        KeyCode.Escape => ImguiKey.Escape,
        KeyCode.Tab => ImguiKey.Tab,
        KeyCode.Return => ImguiKey.Return,
        KeyCode.KeypadEnter => ImguiKey.KeypadEnter,
        KeyCode.UpArrow => ImguiKey.UpArrow,
        KeyCode.DownArrow => ImguiKey.DownArrow,
        _ => ImguiKey.None,
    };

    // ── styles ────────────────────────────────────────────────────────────────
    internal static GUIStyle Style(TextStyleHandle handle) => handle is UnityTextStyle style
        ? style.Style
        : throw new ArgumentException($"'{handle?.GetType().FullName}' is not a style this framework draws with.", nameof(handle));

    // The engine's copy of a style a mod took off the skin, read into the handle for the values this build
    // reports back. Everything else stays where it is, on the style.
    internal static void Seed(GUIStyle source, TextStyleHandle target)
    {
        target.Font = source.font == null ? null : new UnityFontHandle(source.font);
        target.WordWrap = source.wordWrap;
        Read(source.padding, target.Padding);
        Read(source.margin, target.Margin);
    }

    // Writes what a mod changed back into the engine style; a value nobody named is left exactly as the skin
    // drew it. The engine only offers setters for the states and the offsets it calls m_Normal, m_Focused,
    // m_Padding and m_Margin, so those are the ones written through.
    internal static void Write(TextStyleHandle source, GUIStyle target)
    {
        if (source.HasFont)
            target.font = Resolve(source.Font);

        if (source.WrittenFontSize is { } fontSize)
            target.fontSize = fontSize;

        if (source.WrittenWordWrap is { } wordWrap)
            target.wordWrap = wordWrap;

        if (source.WrittenRichText is { } richText)
            target.richText = richText;

        if (source.WrittenAlignment is { } alignment)
            target.alignment = Anchor(alignment);

        if (source.WrittenFontStyle is { } fontStyle)
            target.fontStyle = Font(fontStyle);

        // The states and offsets this build lets a caller write are its m_ fields; the properties the engine
        // usually offers (normal, focused, padding, margin) are read only or missing, so the field wins and the
        // property is the fallback for a build where the field is not there.
        Write(source.Normal, target.m_Normal ?? target.normal);
        Write(source.Focused, target.m_Focused);
        Write(source.Padding, target.m_Padding ?? target.padding);
        Write(source.Margin, target.m_Margin ?? target.margin);
    }

    private static void Read(RectOffset source, ImguiOffsets target)
    {
        target.Left = source.left;
        target.Right = source.right;
        target.Top = source.top;
        target.Bottom = source.bottom;
    }

    // Mutating the state the style already owns is the only way its colour and background can be set: the
    // engine has no getters for them, so a state built here would be a state outside the style.
    private static void Write(ImguiStyleState source, GUIStyleState? target)
    {
        if (target == null)
            return;

        if (source.WrittenTextColor is { } textColor)
            target.textColor = textColor.ToUnity();

        if (source.HasBackground)
            target.background = ResolveBackground(source.WrittenBackground);
    }

    private static void Write(ImguiOffsets source, RectOffset? target)
    {
        if (target == null)
            return;

        if (source.WrittenLeft is { } left)
            target.left = left;

        if (source.WrittenRight is { } right)
            target.right = right;

        if (source.WrittenTop is { } top)
            target.top = top;

        if (source.WrittenBottom is { } bottom)
            target.bottom = bottom;
    }

    private static ImguiTextAnchor Anchor(TextAnchor anchor) => anchor switch
    {
        TextAnchor.UpperLeft => ImguiTextAnchor.UpperLeft,
        TextAnchor.UpperCenter => ImguiTextAnchor.UpperCenter,
        TextAnchor.UpperRight => ImguiTextAnchor.UpperRight,
        TextAnchor.MiddleLeft => ImguiTextAnchor.MiddleLeft,
        TextAnchor.MiddleCenter => ImguiTextAnchor.MiddleCenter,
        TextAnchor.MiddleRight => ImguiTextAnchor.MiddleRight,
        TextAnchor.LowerLeft => ImguiTextAnchor.LowerLeft,
        TextAnchor.LowerCenter => ImguiTextAnchor.LowerCenter,
        _ => ImguiTextAnchor.LowerRight,
    };

    private static TextAnchor Anchor(ImguiTextAnchor anchor) => anchor switch
    {
        ImguiTextAnchor.UpperLeft => TextAnchor.UpperLeft,
        ImguiTextAnchor.UpperCenter => TextAnchor.UpperCenter,
        ImguiTextAnchor.UpperRight => TextAnchor.UpperRight,
        ImguiTextAnchor.MiddleLeft => TextAnchor.MiddleLeft,
        ImguiTextAnchor.MiddleCenter => TextAnchor.MiddleCenter,
        ImguiTextAnchor.MiddleRight => TextAnchor.MiddleRight,
        ImguiTextAnchor.LowerLeft => TextAnchor.LowerLeft,
        ImguiTextAnchor.LowerCenter => TextAnchor.LowerCenter,
        ImguiTextAnchor.LowerRight => TextAnchor.LowerRight,
        _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "Unknown text anchor."),
    };

    private static ImguiFontStyle Font(FontStyle style) => style switch
    {
        UnityEngine.FontStyle.Normal => ImguiFontStyle.Normal,
        UnityEngine.FontStyle.Bold => ImguiFontStyle.Bold,
        UnityEngine.FontStyle.Italic => ImguiFontStyle.Italic,
        _ => ImguiFontStyle.BoldAndItalic,
    };

    private static UnityEngine.FontStyle Font(ImguiFontStyle style) => style switch
    {
        ImguiFontStyle.Normal => UnityEngine.FontStyle.Normal,
        ImguiFontStyle.Bold => UnityEngine.FontStyle.Bold,
        ImguiFontStyle.Italic => UnityEngine.FontStyle.Italic,
        ImguiFontStyle.BoldAndItalic => UnityEngine.FontStyle.BoldAndItalic,
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown font style."),
    };

    // ── textures and fonts ────────────────────────────────────────────────────
    internal static Texture Resolve(TextureHandle handle) => handle switch
    {
        null => throw new ArgumentNullException(nameof(handle)),
        UnityTextureHandle mirror => mirror.Texture,
        _ when ReferenceEquals(handle, TextureHandle.White) => Texture2D.whiteTexture,
        _ => throw new ArgumentException($"'{handle.GetType().FullName}' is not a texture this framework draws with.", nameof(handle)),
    };

    // A style backs a control with a texture, and the engine only takes a Texture2D there; a texture that is
    // not one is dropped rather than thrown at, since a style that silently has no background still draws.
    private static Texture2D? ResolveBackground(TextureHandle? handle) => handle switch
    {
        null => null,
        UnityTextureHandle mirror => mirror.Texture as Texture2D,
        _ when ReferenceEquals(handle, TextureHandle.White) => Texture2D.whiteTexture,
        _ => throw new ArgumentException($"'{handle.GetType().FullName}' is not a texture this framework draws with.", nameof(handle)),
    };

    private static Font? Resolve(FontHandle? handle) => handle switch
    {
        null => null,
        UnityFontHandle mirror => mirror.Font,
        _ => throw new ArgumentException($"'{handle.GetType().FullName}' is not a font this framework draws with.", nameof(handle)),
    };

    internal static ScaleMode Resolve(ImguiScaleMode mode) => mode switch
    {
        ImguiScaleMode.StretchToFill => ScaleMode.StretchToFill,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown scale mode."),
    };
}

// ── handles ───────────────────────────────────────────────────────────────────

internal sealed class UnityTextStyle : TextStyleHandle
{
    private GUIStyle? _style;
    private int _written = -1;

    // A handle with no engine style behind it; the style is built the first time it is used, so a handle can
    // be built while the engine is not there.
    internal UnityTextStyle()
    {
    }

    // A mod's own style, taken off the skin or off another engine style: the engine copy carries the whole
    // look, and the handle carries whichever values this build reports back.
    internal UnityTextStyle(GUIStyle source)
    {
        _style = new GUIStyle(source);
        ImguiMirror.Seed(_style, this);
    }

    internal GUIStyle Style
    {
        get
        {
            _style ??= new GUIStyle();
            if (_written != Version)
            {
                ImguiMirror.Write(this, _style);
                _written = Version;
            }

            return _style;
        }
    }

    public override MirrorVector2 CalcSize(string text)
    {
        var size = Style.CalcSize(new GUIContent(text));
        return new MirrorVector2(size);
    }

    public override float CalcHeight(string text, float width) => Style.CalcHeight(new GUIContent(text), width);

    // Cloning a handle that was never used stays engine free: there is no engine style to copy yet, so the
    // clone starts from the skin's defaults plus the writes the source carries.
    public override TextStyleHandle Clone()
    {
        var clone = _style is null ? new UnityTextStyle() : new UnityTextStyle(_style);
        clone.CopyWritesFrom(this);
        return clone;
    }
}

internal sealed class UnitySkin : SkinHandle
{
    private readonly GUISkin _skin;

    internal UnitySkin(GUISkin skin) => _skin = skin;

    public override TextStyleHandle Label => new UnityTextStyle(_skin.label);

    public override TextStyleHandle TextField => new UnityTextStyle(_skin.textField);

    public override TextStyleHandle Button => new UnityTextStyle(_skin.button);

    public override FontHandle? Font => _skin.font == null ? null : new UnityFontHandle(_skin.font);
}

internal sealed class UnityFontHandle : FontHandle
{
    internal UnityFontHandle(Font font) => Font = font;

    internal Font Font { get; }
}

internal sealed class UnityTextureHandle : TextureHandle
{
    internal UnityTextureHandle(Texture texture) => Texture = texture;

    internal Texture Texture { get; }
}

internal sealed class UnityTextInputState : TextInputState
{
    private readonly TextEditor _editor;

    internal UnityTextInputState(TextEditor editor) => _editor = editor;

    public override int CursorIndex
    {
        get => _editor.cursorIndex;
        set => _editor.cursorIndex = value;
    }

    public override int SelectIndex
    {
        get => _editor.selectIndex;
        set => _editor.selectIndex = value;
    }
}
