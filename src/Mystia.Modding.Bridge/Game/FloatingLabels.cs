using System.Collections;
using Mystia.Assets;
using Mystia.Scenes;
using TMPro;
using UnityEngine;

namespace Mystia.Modding.Bridge;

// The engine side of the floating labels: the host handle, the font and outline every label is drawn with, the
// two shapes the mod's FloatingTextHelper carried (a temporary line that fades, a nameplate that lives with its
// host) and their fade. Everything here names an engine type, which is why it lives under Game/.

/// <summary>The host object a label hangs on: the transform is kept for the label's whole life.</summary>
internal sealed class UnityTransformHandle : TransformHandle
{
    internal UnityTransformHandle(Transform transform) => Transform = transform;

    internal Transform Transform { get; }
}

/// <summary>
/// Builds and drives the labels. One font is created once from the system's CJK face (falling back to a font
/// the game already loaded) and one outlined material is shared by every label, the same way the mod's own
/// helper did it, so a label costs no font work.
/// </summary>
internal static class FloatingLabels
{
    private const float OutlineWidth = 0.05f;

    private static TMP_FontAsset? _font;
    private static bool _fontSearched;
    private static Material? _material;

    internal static TransformHandle? Bind(object host) => host switch
    {
        Transform transform => new UnityTransformHandle(transform),
        GameObject gameObject => new UnityTransformHandle(gameObject.transform),
        Component component => new UnityTransformHandle(component.transform),
        _ => null,
    };

    // The temporary line: it fades and destroys itself, a lifetime of zero fading it at once.
    internal static IFloatingLabel? Spawn(Transform parent, string text, FloatingLabelStyle style, float lifetimeSeconds)
    {
        var created = Create(parent, text, style);
        return created is null ? null : new EngineFloatingLabel(created, fading: true, lifetimeSeconds);
    }

    // The nameplate: it stays until it is stopped or its host goes away.
    internal static IFloatingLabel? Attach(Transform parent, string text, FloatingLabelStyle style)
    {
        var created = Create(parent, text, style);
        return created is null ? null : new EngineFloatingLabel(created, fading: false, 0f);
    }

    private static TextMeshPro? Create(Transform parent, string text, FloatingLabelStyle style)
    {
        if (parent == null)
            return null;

        try
        {
            var host = new GameObject("MystiaLabel");
            host.transform.SetParent(parent, false);
            var offset = style.Offset;
            host.transform.localPosition = new Vector3(offset.X, offset.Y, offset.Z);

            var label = host.AddComponent<TextMeshPro>();
            label.text = text;
            Apply(label, style);
            return label;
        }
        catch (Exception error)
        {
            GameBridgeHook.Trace($"FloatingLabels: a label could not be built: {error.GetBaseException().Message}");
            return null;
        }
    }

    private static void Apply(TextMeshPro label, FloatingLabelStyle style)
    {
        var font = Font();
        if (font is not null)
            label.font = font;

        var color = style.Color;
        label.fontSize = style.FontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(color.R, color.G, color.B, color.A);

        var material = Outlined(label);
        if (material is not null)
            label.fontSharedMaterial = material;
    }

    // The CJK face the system carries, or, when it cannot be built, a font the game already loaded. Both are
    // searched once: a label is created per line, and the search is not cheap.
    private static TMP_FontAsset? Font()
    {
        if (_font is not null || _fontSearched)
            return _font;
        _fontSearched = true;

        try
        {
            var system = UnityEngine.Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 48);
            if (system is not null && TMP_FontAsset.CreateFontAsset(system) is { } created)
                _font = created;
        }
        catch (Exception error)
        {
            GameBridgeHook.Trace($"FloatingLabels: a font could not be built from the system: {error.GetBaseException().Message}");
        }

        if (_font is null)
        {
            try
            {
                var loaded = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (loaded is not null && loaded.Length > 0)
                {
                    foreach (var candidate in loaded)
                    {
                        if (candidate is not null && candidate.name is not null
                            && (candidate.name.Contains("YaHei", StringComparison.Ordinal)
                                || candidate.name.Contains("CJK", StringComparison.Ordinal)))
                        {
                            _font = candidate;
                            break;
                        }
                    }

                    _font ??= loaded[0];
                }
            }
            catch (Exception error)
            {
                GameBridgeHook.Trace($"FloatingLabels: no font could be found in the game's own assets: {error.GetBaseException().Message}");
            }
        }

        return _font;
    }

    // The outline the mod's own labels used: a black outline plus a soft underlay, so white text stays legible
    // over bright art. Built once from the font's own material.
    private static Material? Outlined(TextMeshPro label)
    {
        if (_material is not null)
            return _material;

        var source = label.fontSharedMaterial;
        if (source is null)
            return null;

        var material = new Material(source);
        material.EnableKeyword("OUTLINE_ON");
        material.SetFloat("_OutlineWidth", OutlineWidth);
        material.SetColor("_OutlineColor", Color.black);
        material.EnableKeyword("UNDERLAY_ON");
        material.SetFloat("_UnderlayOffsetX", 0f);
        material.SetFloat("_UnderlayOffsetY", 0f);
        material.SetFloat("_UnderlayDilate", 0.3f);
        material.SetColor("_UnderlayColor", Color.black);
        _material = material;
        return material;
    }
}

/// <summary>
/// One label. A temporary one (<paramref name="lifetimeSeconds"/> above zero) fades out after its lifetime and
/// destroys itself; a nameplate stays until it is stopped or its host goes away. Both ends are idempotent, and
/// the handles compare by reference.
/// </summary>
internal sealed class EngineFloatingLabel : IFloatingLabel
{
    /// <summary>How long the text takes to fade out once its lifetime is over.</summary>
    private const float FadeSeconds = 0.5f;

    private readonly CoroutineScheduler _dispatcher = CoroutineScheduler.Shared;

    private TextMeshPro? _label;
    private CoroutineHandle _fade;
    private bool _fading;

    internal EngineFloatingLabel(TextMeshPro label, bool fading, float lifetimeSeconds)
    {
        _label = label;
        if (!fading)
            return;

        var dispatcher = _dispatcher;
        _fade = dispatcher.StartOn(dispatcher.Owner, _ => Fade(label, lifetimeSeconds));
        _fading = true;
    }

    public bool SetText(string text)
    {
        if (_label is null || string.IsNullOrWhiteSpace(text))
            return false;
        _label.text = text;
        return true;
    }

    public void SetVisible(bool visible)
    {
        var label = _label;
        if (label is null || label.gameObject is null)
            return;
        label.gameObject.SetActive(visible);
    }

    public void Stop()
    {
        var label = _label;
        _label = null;
        if (_fading)
        {
            _fading = false;
            _dispatcher.Stop(_fade);
        }

        if (label is not null && label.gameObject is not null)
            UnityEngine.Object.Destroy(label.gameObject);
    }

    public void Dispose() => Stop();

    // The lifetime wait and the fade out the mod's own helper used, on the scene dispatcher: a label whose
    // object the scene destroyed ends the routine itself.
    private static IEnumerator Fade(TextMeshPro label, float lifetimeSeconds)
    {
        for (var elapsed = 0f; elapsed < lifetimeSeconds; elapsed += Time.deltaTime)
        {
            if (label == null)
                yield break;
            yield return null;
        }

        for (var faded = 0f; faded < FadeSeconds; faded += Time.deltaTime)
        {
            if (label == null)
                yield break;
            var color = label.color;
            color.a = Mathf.Lerp(1f, 0f, faded / FadeSeconds);
            label.color = color;
            yield return null;
        }

        if (label != null && label.gameObject != null)
            UnityEngine.Object.Destroy(label.gameObject);
    }
}
