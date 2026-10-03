using Mystia.Numerics;

namespace Mystia.Imgui;

/// <summary>Where the text of a <see cref="TextStyleHandle"/> sits inside its rectangle.</summary>
public enum ImguiTextAnchor
{
    /// <summary>Top left.</summary>
    UpperLeft,

    /// <summary>Top centre.</summary>
    UpperCenter,

    /// <summary>Top right.</summary>
    UpperRight,

    /// <summary>Middle left.</summary>
    MiddleLeft,

    /// <summary>Centre.</summary>
    MiddleCenter,

    /// <summary>Middle right.</summary>
    MiddleRight,

    /// <summary>Bottom left.</summary>
    LowerLeft,

    /// <summary>Bottom centre.</summary>
    LowerCenter,

    /// <summary>Bottom right.</summary>
    LowerRight,
}

/// <summary>Weight and slant of a <see cref="TextStyleHandle"/>'s font.</summary>
public enum ImguiFontStyle
{
    /// <summary>Upright and regular.</summary>
    Normal,

    /// <summary>Bold.</summary>
    Bold,

    /// <summary>Italic.</summary>
    Italic,

    /// <summary>Bold and italic.</summary>
    BoldAndItalic,
}

/// <summary>
/// The colour and backing texture one control state draws with, the mirror of the engine's style state.
/// Reachable as <see cref="TextStyleHandle.Normal"/> and <see cref="TextStyleHandle.Focused"/>; a mod writes
/// through those properties instead of building one.
/// </summary>
public sealed class ImguiStyleState
{
    private readonly Action _changed;
    private Color? _textColor;
    private TextureHandle? _background;
    private bool _backgroundSet;

    internal ImguiStyleState(Action changed) => _changed = changed;

    /// <summary>The colour the text is drawn in; <see cref="Color.Black"/> until a mod sets one.</summary>
    public Color TextColor
    {
        get => _textColor ?? Color.Black;
        set
        {
            if (_textColor == value)
                return;

            _textColor = value;
            _changed();
        }
    }

    /// <summary>The texture painted behind the control; <see langword="null"/> until a mod sets one.</summary>
    public TextureHandle? Background
    {
        get => _background;
        set
        {
            if (_backgroundSet && ReferenceEquals(_background, value))
                return;

            _background = value;
            _backgroundSet = true;
            _changed();
        }
    }

    // A style only carries what a mod wrote: the engine keeps the value it already had for everything left
    // alone, so the framework has to tell "not written" from "written as the default".
    internal Color? WrittenTextColor => _textColor;

    internal bool HasBackground => _backgroundSet;

    internal TextureHandle? WrittenBackground => _background;
}

/// <summary>
/// The four side offsets a style pads or margins with, the mirror of the engine's rect offset. Reachable as
/// <see cref="TextStyleHandle.Padding"/> and <see cref="TextStyleHandle.Margin"/>.
/// </summary>
public sealed class ImguiOffsets
{
    private readonly Action _changed;
    private int? _left;
    private int? _right;
    private int? _top;
    private int? _bottom;

    internal ImguiOffsets(Action changed) => _changed = changed;

    /// <summary>The left offset.</summary>
    public int Left
    {
        get => _left ?? 0;
        set => Set(ref _left, value);
    }

    /// <summary>The right offset.</summary>
    public int Right
    {
        get => _right ?? 0;
        set => Set(ref _right, value);
    }

    /// <summary>The top offset.</summary>
    public int Top
    {
        get => _top ?? 0;
        set => Set(ref _top, value);
    }

    /// <summary>The bottom offset.</summary>
    public int Bottom
    {
        get => _bottom ?? 0;
        set => Set(ref _bottom, value);
    }

    internal int? WrittenLeft => _left;

    internal int? WrittenRight => _right;

    internal int? WrittenTop => _top;

    internal int? WrittenBottom => _bottom;

    private void Set(ref int? field, int value)
    {
        if (field == value)
            return;

        field = value;
        _changed();
    }
}

// A style a mod draws with. What the handle holds is what the mod wrote, not a copy of the engine style: the
// engine build only reports a few style values back (the font, word wrap, padding and margin), so the engine
// style is where the rest of a style's look lives. A value a mod writes is pushed into the engine style when
// that style is next used to draw or to measure, and a value a mod leaves alone keeps whatever the skin gave
// the style — which is exactly what the engine's own
//
//     new GUIStyle(drawer.Skin.Label) { FontSize = 14 }
//
// means, and it is why the handle is the source of truth only for what a mod wrote.
/// <summary>
/// A style a control is drawn with. A mod starts one from the skin — <see cref="SkinHandle.Label"/>,
/// <see cref="SkinHandle.TextField"/> or <see cref="SkinHandle.Button"/> — changes the values it cares about
/// and passes it back to the drawer; there is no public way to build one, so a style always belongs to a skin.
/// <para>
/// What a mod changes is what the style becomes: a value it never writes keeps the look the skin gave the
/// style, which is how a mod restyles a button without losing the skin's own button art. Reading a value back
/// gives what the mod wrote, or — for a style taken from the skin — what the engine reported when the style
/// was taken, and the framework's default beyond that. Only the members below are part of the contract; the
/// engine's style carries far more, and the framework adds what a mod asks for rather than opening the whole
/// surface.
/// </para>
/// </summary>
public abstract class TextStyleHandle
{
    private FontHandle? _font;
    private bool _fontSet;
    private int? _fontSize;
    private bool? _wordWrap;
    private bool? _richText;
    private ImguiTextAnchor? _alignment;
    private ImguiFontStyle? _fontStyle;

    internal TextStyleHandle()
    {
        Normal = new ImguiStyleState(Changed);
        Focused = new ImguiStyleState(Changed);
        Padding = new ImguiOffsets(Changed);
        Margin = new ImguiOffsets(Changed);
    }

    /// <summary>The font the text is drawn with, or <see langword="null"/> to keep the skin's font.</summary>
    public FontHandle? Font
    {
        get => _font;
        set
        {
            if (_fontSet && ReferenceEquals(_font, value))
                return;

            _font = value;
            _fontSet = true;
            Changed();
        }
    }

    /// <summary>The size of the text; zero asks the engine for its default size.</summary>
    public int FontSize
    {
        get => _fontSize ?? 0;
        set => Set(ref _fontSize, value);
    }

    /// <summary>Whether long text is wrapped to the width of the rectangle instead of clipped.</summary>
    public bool WordWrap
    {
        get => _wordWrap ?? false;
        set => Set(ref _wordWrap, value);
    }

    /// <summary>Whether rich text tags such as <c>&lt;color=#FFCC66&gt;</c> in the text are interpreted.</summary>
    public bool RichText
    {
        get => _richText ?? false;
        set => Set(ref _richText, value);
    }

    /// <summary>Where the text sits inside the rectangle.</summary>
    public ImguiTextAnchor Alignment
    {
        get => _alignment ?? ImguiTextAnchor.UpperLeft;
        set => Set(ref _alignment, value);
    }

    /// <summary>Weight and slant of the font.</summary>
    public ImguiFontStyle FontStyle
    {
        get => _fontStyle ?? ImguiFontStyle.Normal;
        set => Set(ref _fontStyle, value);
    }

    /// <summary>The colour and backing texture of an idle control.</summary>
    public ImguiStyleState Normal { get; }

    /// <summary>The colour and backing texture of a focused control, a text field for instance.</summary>
    public ImguiStyleState Focused { get; }

    /// <summary>The space between the rectangle and what it draws.</summary>
    public ImguiOffsets Padding { get; }

    /// <summary>The space between the rectangle and its neighbours.</summary>
    public ImguiOffsets Margin { get; }

    /// <summary>
    /// How large <paramref name="text"/> is drawn with this style, which is how a mod sizes a row to the text
    /// it holds.
    /// </summary>
    public abstract Vector2 CalcSize(string text);

    /// <summary>
    /// How tall <paramref name="text"/> is drawn with this style when it is given
    /// <paramref name="width"/> to wrap in.
    /// </summary>
    public abstract float CalcHeight(string text, float width);

    /// <summary>
    /// A second style carrying what this one carries, the engine's <c>new GUIStyle(other)</c>: a mod clones a
    /// style it already restyled when it needs two variants of it, and the two then change separately.
    /// </summary>
    public abstract TextStyleHandle Clone();

    // Bumped by every write above, so the framework can tell a style it has already pushed into the engine
    // style from one a mod changed since. It is bookkeeping, not a value a mod reads.
    internal int Version { get; private set; }

    // What a mod actually wrote. A null here means the engine style keeps the value it already has, which is
    // the whole point of the handle: a skin style restyled in one place keeps its look everywhere else.
    internal bool HasFont => _fontSet;

    internal FontHandle? WrittenFont => _font;

    internal int? WrittenFontSize => _fontSize;

    internal bool? WrittenWordWrap => _wordWrap;

    internal bool? WrittenRichText => _richText;

    internal ImguiTextAnchor? WrittenAlignment => _alignment;

    internal ImguiFontStyle? WrittenFontStyle => _fontStyle;

    private void Changed() => ++Version;

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        Changed();
    }

    // Copies what a mod wrote on another handle onto this one, which is all a clone has to carry: a value the
    // source never wrote stays unwritten here too, so the engine style of the clone keeps the look it copied.
    internal void CopyWritesFrom(TextStyleHandle source)
    {
        if (source.HasFont)
            Font = source.WrittenFont;

        if (source.WrittenFontSize is { } fontSize)
            FontSize = fontSize;

        if (source.WrittenWordWrap is { } wordWrap)
            WordWrap = wordWrap;

        if (source.WrittenRichText is { } richText)
            RichText = richText;

        if (source.WrittenAlignment is { } alignment)
            Alignment = alignment;

        if (source.WrittenFontStyle is { } fontStyle)
            FontStyle = fontStyle;

        CopyStyleState(Normal, source.Normal);
        CopyStyleState(Focused, source.Focused);
        CopyOffsets(Padding, source.Padding);
        CopyOffsets(Margin, source.Margin);
    }

    private static void CopyStyleState(ImguiStyleState target, ImguiStyleState source)
    {
        if (source.WrittenTextColor is { } textColor)
            target.TextColor = textColor;

        if (source.HasBackground)
            target.Background = source.WrittenBackground;
    }

    private static void CopyOffsets(ImguiOffsets target, ImguiOffsets source)
    {
        if (source.WrittenLeft is { } left)
            target.Left = left;

        if (source.WrittenRight is { } right)
            target.Right = right;

        if (source.WrittenTop is { } top)
            target.Top = top;

        if (source.WrittenBottom is { } bottom)
            target.Bottom = bottom;
    }
}
