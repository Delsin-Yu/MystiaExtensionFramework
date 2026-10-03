using Common.UI;
using GameData.Core.Collections.NightSceneUtility;
using Mystia.Assets;
using Mystia.Scenes;
using NumericsVector3 = Mystia.Numerics.Vector3;
using UnityEngine;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The presentation half of a scene session (see <see cref="IPresentationServices"/>).
///
/// These members act on the scene that is running right now, so unlike <see cref="ICommonServices"/> they are
/// not handed out to every loop: a scene services instance exposes them through
/// <c>Presentation</c>, and <see cref="ServiceScope.Require"/> keeps a call outside that scene loop's
/// Setup/Update/Shutdown (or after the scene was replaced) from touching the wrong scene.
///
/// An asset is named by the key it was filed under, so the shared service never needs to know which mod called
/// it: audio comes from <c>IAssetLocator.TryRegisterAudioClip</c>, effect templates from
/// <see cref="TryRegisterPrefab"/> (or from the framework's own map path), and the engine work lives in
/// <see cref="EffectVfx"/>, <see cref="EffectPrefabs"/>, <see cref="SceneAudio"/>,
/// <see cref="FloatingLabels"/> and <see cref="CharacterSprites"/>.
/// </summary>
internal sealed partial class PresentationServices : IPresentationServices
{
    /// <summary>The instance every scene services object hands out as <c>Presentation</c>.</summary>
    internal static readonly PresentationServices Shared = new();

    /// <summary>Asset paths already reported, so a per frame call cannot flood the host log.</summary>
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    public void ShakeCamera(float duration, float strength, float frequency)
    {
        ServiceScope.Require();
        // The game takes the strength first, then the duration, then the fade out, where the service takes
        // them (duration, strength, frequency).
        UniversalGameManager.SetCameraShake(strength, duration, frequency);
    }

    public IVfxHandle? PlayVfx(string assetPath, NumericsVector3 position)
    {
        ServiceScope.Require();
        if (string.IsNullOrWhiteSpace(assetPath))
            return null;
        if (!AssetLocator.Shared.TryResolveGameObject(assetPath, out var template))
        {
            // Nothing is filed under the key, so nothing plays: the miss is reported once and answers null,
            // which is the same "no effect" a caller gets from an empty path.
            Report("PlayVfx", assetPath);
            return null;
        }

        return EffectVfx.Play(template, new Vector3(position.X, position.Y, position.Z));
    }

    public IVfxHandle? PlayScreenOverlay(string assetPath)
    {
        ServiceScope.Require();
        if (string.IsNullOrWhiteSpace(assetPath))
            return null;
        if (!AssetLocator.Shared.TryResolveGameObject(assetPath, out var template))
        {
            Report("PlayScreenOverlay", assetPath);
            return null;
        }

        return EffectVfx.PlayOverlay(template) ?? InertVfxHandle.Instance;
    }

    public void PlayAudio(string assetPath)
    {
        ServiceScope.Require();
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            Report("PlayAudio", assetPath);
            return;
        }

        if (!AssetLocator.Shared.TryResolveAudioClip(assetPath, out var handle) || handle is not UnityAudioClipHandle clip)
        {
            Report("PlayAudio", assetPath);
            return;
        }

        SceneAudio.Play(clip.Clip);
    }

    public bool TryRegisterPrefab(string key, object source)
    {
        ServiceScope.Require();
        return EffectPrefabs.TryRegister(key, source);
    }

    public TransformHandle? Bind(object host)
    {
        ServiceScope.Require();
        return FloatingLabels.Bind(host);
    }

    public IFloatingLabel? SpawnLabel(TransformHandle host, string text, FloatingLabelStyle style, float lifetimeSeconds = 5f)
    {
        ServiceScope.Require();
        if (host is not UnityTransformHandle target || string.IsNullOrWhiteSpace(text))
            return null;
        if (!float.IsFinite(lifetimeSeconds) || lifetimeSeconds < 0f || !Style(style))
            return null;

        return FloatingLabels.Spawn(target.Transform, text, style, lifetimeSeconds);
    }

    public IFloatingLabel? AttachLabel(TransformHandle host, string text, FloatingLabelStyle style)
    {
        ServiceScope.Require();
        if (host is not UnityTransformHandle target || string.IsNullOrWhiteSpace(text) || !Style(style))
            return null;

        return FloatingLabels.Attach(target.Transform, text, style);
    }

    public NumericsVector3 PlayerPosition
    {
        get
        {
            ServiceScope.Require();
            // The game's own player origin (the same call the game's spells make through SpellBase).
            var position = SpellBase.GetPlayerPosition();
            return new NumericsVector3(position.x, position.y, position.z);
        }
    }

    public NumericsVector3 TablePosition(int deskCode)
    {
        ServiceScope.Require();
        // The game's own table target, i.e. what a spell aims its delivery at for that desk.
        var position = SpellBase.GetGuestTable(deskCode);
        return new NumericsVector3(position.x, position.y, position.z);
    }

    // The style an engine label can be built from; anything the engine would mangle is refused before a
    // GameObject is created.
    private static bool Style(FloatingLabelStyle style)
    {
        var color = style.Color;
        if (!float.IsFinite(color.R) || !float.IsFinite(color.G) || !float.IsFinite(color.B) || !float.IsFinite(color.A))
            return false;
        var offset = style.Offset;
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y) || !float.IsFinite(offset.Z))
            return false;
        return float.IsFinite(style.FontSize) && style.FontSize > 0f;
    }

    /// <summary>
    /// A kind of call is reported once per asset path: a mod that asks for a missing key from its update loop
    /// must not flood the host log, but a real miss still has to be visible in it.
    /// </summary>
    private static void Report(string member, string assetPath)
    {
        if (!Reported.Add(member + " " + assetPath))
            return;
        GameBridgeHook.Trace($"{member}: nothing is filed under '{assetPath}'; the call was ignored.");
    }

    /// <summary>The handle a miss yields: a real handle, comparable and safe to stop repeatedly.</summary>
    private sealed class InertVfxHandle : IVfxHandle
    {
        internal static readonly InertVfxHandle Instance = new();

        public void Stop()
        {
        }
    }
}
