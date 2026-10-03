using System.Reflection;
using Mystia.Imgui;
using Mystia.Modding.Bridge;
using Mystia.Numerics;
using Xunit;

namespace Mystia.Tests;

public class ImguiTests
{
    /// <summary>
    /// The whole point of the facade: a mod may only name framework types, so no member of the drawer may
    /// name anything from the engine or the interop, value types included.
    /// </summary>
    [Fact]
    public void Drawer_members_never_name_the_engine()
    {
        var drawer = typeof(IIMGUIDrawer);
        var framework = drawer.Assembly;
        var banned = new[] { "UnityEngine", "Il2Cpp", "Il2CppInterop" };

        foreach (var member in drawer.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            foreach (var type in TypesNamedBy(member))
            {
                var name = type.Namespace ?? string.Empty;

                Assert.DoesNotContain(banned, prefix => name.StartsWith(prefix, StringComparison.Ordinal));
                Assert.True(
                    type.IsGenericParameter
                    || type.Assembly == framework
                    || name.StartsWith("System", StringComparison.Ordinal),
                    $"{drawer.Name}.{member.Name} names {type.FullName}, which does not come from the framework.");
            }
        }
    }

    /// <summary>
    /// The handles a mod draws with are framework types too, so nothing a mod passes to the drawer can be an
    /// engine object it built itself.
    /// </summary>
    [Fact]
    public void Drawer_types_are_framework_types()
    {
        foreach (var type in new[]
                 {
                     typeof(IIMGUIDrawer),
                     typeof(ImguiEvent),
                     typeof(ImguiEventKind),
                     typeof(ImguiKey),
                     typeof(TextStyleHandle),
                     typeof(ImguiStyleState),
                     typeof(ImguiOffsets),
                     typeof(ImguiTextAnchor),
                     typeof(ImguiFontStyle),
                     typeof(SkinHandle),
                     typeof(FontHandle),
                     typeof(TextureHandle),
                     typeof(ImguiScaleMode),
                     typeof(TextInputState),
                 })
        {
            Assert.Equal("Mystia", type.Namespace?.Split('.')[0]);
            Assert.Equal("Mystia.Net.Sdk", type.Assembly.GetName().Name);
        }
    }

    /// <summary>
    /// A mod may not build a handle, so the framework hands the one texture every mod panel needs out itself:
    /// the engine's white texture, which the drawer has to produce without touching the engine.
    /// </summary>
    [Fact]
    public void Drawer_hands_out_the_built_in_white_texture()
    {
        var drawer = new ImguiDrawer();

        Assert.NotNull(drawer.WhiteTexture);
        Assert.Same(TextureHandle.White, drawer.WhiteTexture);
        Assert.Same(drawer.WhiteTexture, drawer.WhiteTexture);
    }

    /// <summary>
    /// Use() consumes the event the projection came from, but only for the events the engine lets a mod
    /// consume: the layout and repaint passes are never consumed, and neither is an event the facade does not
    /// model, so a call the engine would reject cannot arrive from a mod.
    /// </summary>
    [Fact]
    public void Use_consumes_only_an_event_the_engine_lets_a_mod_consume()
    {
        foreach (var kind in new[]
                 {
                     ImguiEventKind.KeyDown,
                     ImguiEventKind.MouseDown,
                     ImguiEventKind.MouseDrag,
                     ImguiEventKind.MouseUp,
                 })
        {
            var consumed = 0;
            var key = new ImguiEvent(kind, ImguiKey.Escape, shift: true, 'x', new Vector2(3f, 4f), () => consumed++);

            Assert.Equal(kind, key.Kind);
            Assert.Equal(ImguiKey.Escape, key.Key);
            Assert.True(key.Shift);
            Assert.Equal('x', key.Character);
            Assert.Equal(new Vector2(3f, 4f), key.MousePosition);

            key.Use();

            Assert.Equal(1, consumed);
        }

        foreach (var kind in new[] { ImguiEventKind.Layout, ImguiEventKind.Repaint, ImguiEventKind.Other })
        {
            var consumed = 0;
            var passive = new ImguiEvent(kind, ImguiKey.None, shift: false, '\0', Vector2.Zero, () => consumed++);

            passive.Use();

            Assert.Equal(0, consumed);
        }
    }

    /// <summary>A projection with nothing behind it is still safe to consume, and an unused one touches nothing.</summary>
    [Fact]
    public void Use_without_an_event_behind_it_does_nothing()
    {
        default(ImguiEvent).Use();

        var drawn = new ImguiEvent(ImguiEventKind.Repaint, ImguiKey.None, shift: false, '\0', Vector2.Zero);
        drawn.Use();

        Assert.Equal(ImguiEventKind.Repaint, drawn.Kind);
    }

    /// <summary>
    /// A style a mod starts reports the framework's defaults and writes nothing into the engine style it
    /// stands for: every value a mod never names keeps the look the skin gave the style.
    /// </summary>
    [Fact]
    public void Style_handle_defaults_and_writes()
    {
        var style = new UnityTextStyle();

        Assert.Null(style.Font);
        Assert.False(style.HasFont);
        Assert.Equal(0, style.FontSize);
        Assert.False(style.WordWrap);
        Assert.False(style.RichText);
        Assert.Equal(ImguiTextAnchor.UpperLeft, style.Alignment);
        Assert.Equal(ImguiFontStyle.Normal, style.FontStyle);
        Assert.Equal(Color.Black, style.Normal.TextColor);
        Assert.Null(style.Normal.Background);
        Assert.Equal(Color.Black, style.Focused.TextColor);
        Assert.Null(style.Focused.Background);
        Assert.Equal(0, style.Padding.Left);
        Assert.Equal(0, style.Padding.Right);
        Assert.Equal(0, style.Padding.Top);
        Assert.Equal(0, style.Padding.Bottom);
        Assert.Equal(0, style.Margin.Left);
        Assert.Equal(0, style.Margin.Top);

        Assert.Null(style.WrittenFontSize);
        Assert.Null(style.WrittenWordWrap);
        Assert.Null(style.WrittenRichText);
        Assert.Null(style.WrittenAlignment);
        Assert.Null(style.WrittenFontStyle);
        Assert.Null(style.Normal.WrittenTextColor);
        Assert.False(style.Normal.HasBackground);
        Assert.Null(style.Padding.WrittenLeft);

        style.FontSize = 14;
        style.WordWrap = true;
        style.RichText = true;
        style.Alignment = ImguiTextAnchor.MiddleCenter;
        style.FontStyle = ImguiFontStyle.Bold;
        style.Normal.TextColor = new Color(0.8f, 0.8f, 0.85f);
        style.Normal.Background = TextureHandle.White;
        style.Focused.TextColor = Color.White;
        style.Padding.Left = 10;
        style.Padding.Right = 10;
        style.Padding.Top = 4;
        style.Padding.Bottom = 4;
        style.Margin.Top = 2;

        Assert.Equal(14, style.FontSize);
        Assert.True(style.WordWrap);
        Assert.True(style.RichText);
        Assert.Equal(ImguiTextAnchor.MiddleCenter, style.Alignment);
        Assert.Equal(ImguiFontStyle.Bold, style.FontStyle);
        Assert.Equal(new Color(0.8f, 0.8f, 0.85f), style.Normal.TextColor);
        Assert.Same(TextureHandle.White, style.Normal.Background);
        Assert.Equal(Color.White, style.Focused.TextColor);
        Assert.Equal(10, style.Padding.Left);
        Assert.Equal(10, style.Padding.Right);
        Assert.Equal(4, style.Padding.Top);
        Assert.Equal(4, style.Padding.Bottom);
        Assert.Equal(2, style.Margin.Top);

        Assert.Equal(14, style.WrittenFontSize);
        Assert.True(style.WrittenWordWrap);
        Assert.True(style.WrittenRichText);
        Assert.Equal(ImguiTextAnchor.MiddleCenter, style.WrittenAlignment);
        Assert.Equal(ImguiFontStyle.Bold, style.WrittenFontStyle);
        Assert.Equal(new Color(0.8f, 0.8f, 0.85f), style.Normal.WrittenTextColor);
        Assert.True(style.Normal.HasBackground);
        Assert.Same(TextureHandle.White, style.Normal.WrittenBackground);
        Assert.Equal(10, style.Padding.WrittenLeft);
        Assert.Equal(2, style.Margin.WrittenTop);
        Assert.Null(style.Margin.WrittenLeft);
    }

    /// <summary>
    /// A write has to reach the engine style the next draw uses, and a handle that was not written to since
    /// the last draw has nothing to push: the framework counts the writes and syncs the engine style then.
    /// </summary>
    [Fact]
    public void Style_handle_reports_every_write()
    {
        var style = new UnityTextStyle();
        var version = style.Version;

        style.FontSize = 14;
        Assert.True(style.Version > version);

        version = style.Version;
        style.FontSize = 14;
        Assert.Equal(version, style.Version);

        style.Padding.Left = 6;
        Assert.True(style.Version > version);

        version = style.Version;
        style.Normal.Background = TextureHandle.White;
        Assert.True(style.Version > version);
    }

    /// <summary>
    /// Every state and offset group is the style's own: writing one group never reaches another, and the
    /// groups a caller may hold on to are not shared between two styles.
    /// </summary>
    [Fact]
    public void Skin_styles_own_their_groups()
    {
        var style = new UnityTextStyle();
        style.Normal.TextColor = Color.White;

        Assert.NotSame(style.Normal, style.Focused);
        Assert.NotSame(style.Padding, style.Margin);
        Assert.NotSame(style.Normal, new UnityTextStyle().Normal);
        Assert.Equal(Color.Black, style.Focused.TextColor);
        Assert.Equal(Color.Black, new UnityTextStyle().Normal.TextColor);
    }

    /// <summary>
    /// A mod that restyled a style and needs a second variant clones it: the clone carries what the source
    /// wrote, leaves everything the source left alone to the skin, and the two then change separately.
    /// </summary>
    [Fact]
    public void Cloned_style_carries_the_writes_and_stays_independent()
    {
        var source = new UnityTextStyle();
        source.FontSize = 14;
        source.WordWrap = true;
        source.Focused.TextColor = Color.White;
        source.Padding.Left = 6;
        source.Margin.Top = 2;

        var clone = source.Clone();

        Assert.NotSame(source, clone);
        Assert.Equal(14, clone.FontSize);
        Assert.True(clone.WordWrap);
        Assert.Equal(Color.White, clone.Focused.TextColor);
        Assert.Equal(6, clone.Padding.Left);
        Assert.Equal(2, clone.Margin.Top);

        // What the source never wrote stays unwritten, so the clone keeps the skin's own look there too.
        Assert.Null(clone.WrittenRichText);
        Assert.False(clone.HasFont);
        Assert.False(clone.Normal.HasBackground);
        Assert.Null(clone.Normal.WrittenTextColor);

        clone.FontSize = 20;
        clone.Padding.Left = 1;

        Assert.Equal(14, source.FontSize);
        Assert.Equal(6, source.Padding.Left);
        Assert.Equal(20, clone.FontSize);
        Assert.Equal(1, clone.Padding.Left);
    }

    private static IEnumerable<Type> TypesNamedBy(MemberInfo member)
    {
        var named = member switch
        {
            PropertyInfo property => new[] { property.PropertyType }
                .Concat(property.GetIndexParameters().Select(parameter => parameter.ParameterType)),
            MethodInfo method => method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType),
            FieldInfo field => [field.FieldType],
            EventInfo @event when @event.EventHandlerType is { } handler => [handler],
            _ => [],
        };

        return named.SelectMany(Flatten);
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        if (type.IsArray && type.GetElementType() is { } element)
            yield return element;

        foreach (var argument in type.GetGenericArguments())
            yield return argument;
    }
}
