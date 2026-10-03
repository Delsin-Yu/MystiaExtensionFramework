using System.Collections;
using Mystia.Scenes;
using UnityEngine;
using UnityEngine.UI;

namespace Mystia.Modding.Bridge;

// The engine side of the effect and audio layers: filing an effect template a mod hands over, instantiating and
// stopping it, turning a sprite template into a full screen overlay, and playing a clip. It is the work the
// mod's own VfxBundle used to carry, moved behind the presentation seam; nothing here is reachable directly.

/// <summary>
/// The effect template seam. A template is a clone of a game object a mod handed over: the mod keeps its own
/// instance, and the clone is hidden and filed into the runtime asset table the same way the framework's own
/// map templates are. It is stored inactive, so <see cref="EffectVfx.Play"/> has to wake the instance it
/// builds from it.
/// </summary>
internal static class EffectPrefabs
{
    internal static bool TryRegister(string key, object source)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var gameObject = source switch
        {
            GameObject host => host,
            Component component => component.gameObject,
            _ => null,
        };
        if (gameObject is null)
            return false;

        GameObject? template = null;
        try
        {
            template = UnityEngine.Object.Instantiate(gameObject);
            template.name = gameObject.name;
            // A template that stayed active would draw in the scene it was cloned in; the instance play
            // builds from it is woken again.
            template.SetActive(false);
            if (!AssetLocator.Shared.TryRegisterGameObject(key, template, out _))
            {
                UnityEngine.Object.DestroyImmediate(template);
                GameBridgeHook.Trace($"EffectPrefabs: '{key}' could not be filed; the runtime asset table refused it.");
                return false;
            }

            return true;
        }
        catch (Exception error)
        {
            if (template is not null)
                UnityEngine.Object.DestroyImmediate(template);
            GameBridgeHook.Trace($"EffectPrefabs: '{key}' could not be filed: {error.GetBaseException().Message}");
            return false;
        }
    }
}

/// <summary>
/// The effect layer. A template is instantiated at a world position, or read into a full screen overlay; either
/// way the handle it hands back stops it for real: an overlay fades out and is destroyed, a particle effect
/// stops emitting and is destroyed once what is already out has drained.
/// </summary>
internal static class EffectVfx
{
    /// <summary>Above the night HUD (canvas 3000) and below the operational panel root (8000).</summary>
    internal const int OverlaySortingOrder = 7999;

    internal const float OverlayFadeSeconds = 0.8f;

    /// <summary>How long a stopped emitter is given to drain before its object is destroyed.</summary>
    internal const float ParticleDrainSeconds = 6f;

    internal static IVfxHandle? Play(GameObject template, Vector3 position)
    {
        try
        {
            var instance = UnityEngine.Object.Instantiate(template);
            instance.name = template.name;
            instance.transform.position = position;
            // Templates are filed inactive; the instance is what is played.
            instance.SetActive(true);

            // A template that carries a canvas group was authored as an overlay: it starts transparent and
            // fades in, which is what makes Stop's fade out symmetric.
            var group = instance.GetComponent<CanvasGroup>();
            if (group is not null)
            {
                group.alpha = 0f;
                VfxFade.Start(group, 1f, destroy: false);
            }

            return new EngineVfxHandle(instance, group);
        }
        catch (Exception error)
        {
            GameBridgeHook.Trace($"EffectVfx: '{template.name}' could not be played: {error.GetBaseException().Message}");
            return null;
        }
    }

    internal static IVfxHandle? PlayOverlay(GameObject template)
    {
        GameObject? root = null;
        try
        {
            root = new GameObject(template.name);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = OverlaySortingOrder;
            var group = root.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            // A world space renderer cannot cover a screen space overlay canvas, so every sprite layer of the
            // template becomes one full screen image on the overlay, in the template's own draw order.
            var layers = new List<SpriteRenderer>(template.GetComponentsInChildren<SpriteRenderer>(true));
            layers.Sort(static (left, right) => left.sortingOrder.CompareTo(right.sortingOrder));
            foreach (var renderer in layers)
            {
                var layer = new GameObject(renderer.name);
                layer.transform.SetParent(root.transform, false);

                var image = layer.AddComponent<RawImage>();
                var sprite = renderer.sprite;
                if (sprite is not null)
                    image.texture = sprite.texture;
                // The tint the game's own effect shaders use: texture x _TintColor x vertex colour.
                var material = renderer.sharedMaterial;
                image.color = material is not null ? material.GetColor("_TintColor") * renderer.color : renderer.color;
                image.raycastTarget = false;

                var rect = image.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            VfxFade.Start(group, 1f, destroy: false);
            return new EngineVfxHandle(root, group);
        }
        catch (Exception error)
        {
            GameBridgeHook.Trace($"EffectVfx: the overlay '{template.name}' could not be built: {error.GetBaseException().Message}");
            if (root is not null)
                UnityEngine.Object.DestroyImmediate(root);
            return null;
        }
    }
}

/// <summary>
/// Fades a canvas group on the scene's own coroutine dispatcher, so a fade stops with the scene it belongs to.
/// The group may be destroyed under the routine (the scene was unloaded), which ends it.
/// </summary>
internal static class VfxFade
{
    internal static void Start(CanvasGroup group, float to, bool destroy)
    {
        var dispatcher = CoroutineScheduler.Shared;
        dispatcher.StartOn(dispatcher.Owner, _ => Run(group, to, destroy));
    }

    private static IEnumerator Run(CanvasGroup group, float to, bool destroy)
    {
        var from = group.alpha;
        for (var elapsed = 0f; elapsed < EffectVfx.OverlayFadeSeconds; elapsed += Time.deltaTime)
        {
            if (group == null)
                yield break;
            group.alpha = Mathf.Lerp(from, to, elapsed / EffectVfx.OverlayFadeSeconds);
            yield return null;
        }

        if (group == null)
            yield break;
        group.alpha = to;
        if (destroy)
            UnityEngine.Object.Destroy(group.gameObject);
    }
}

/// <summary>Stops what it plays: idempotent, comparable by reference, and safe on a group the scene took.</summary>
internal sealed class EngineVfxHandle : IVfxHandle
{
    private GameObject? _instance;
    private CanvasGroup? _group;
    private bool _stopped;

    internal EngineVfxHandle(GameObject instance, CanvasGroup? group)
    {
        _instance = instance;
        _group = group;
    }

    public void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;

        var instance = _instance;
        var group = _group;
        _instance = null;
        _group = null;
        if (instance is null)
            return;

        if (group is not null)
        {
            VfxFade.Start(group, 0f, destroy: true);
            return;
        }

        foreach (var particles in instance.GetComponentsInChildren<ParticleSystem>(true))
            particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        UnityEngine.Object.Destroy(instance, EffectVfx.ParticleDrainSeconds);
    }
}

/// <summary>
/// Plays one clip through a throwaway audio source. The source lives in the running scene, so the scene
/// unloading takes it and the sound with it; it is destroyed once the clip has finished.
/// </summary>
internal static class SceneAudio
{
    internal static void Play(AudioClip clip)
    {
        try
        {
            var host = new GameObject("MystiaAudio");
            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.clip = clip;
            source.Play();
            UnityEngine.Object.Destroy(host, Mathf.Max(clip.length, 0.1f) + 0.1f);
        }
        catch (Exception error)
        {
            GameBridgeHook.Trace($"SceneAudio: '{clip.name}' could not be played: {error.GetBaseException().Message}");
        }
    }
}
