using Common.UI;
using GameData.Core.Collections.NightSceneUtility;
using GameData.CoreLanguage.Collections;
using GameData.Profile;
using Mystia.Scenes;
using UnityEngine;

namespace Mystia.Modding.Bridge;

/// <summary>
/// The presentation half of a scene session (see <see cref="IPresentationServices"/>).
///
/// These members act on the scene that is running right now, so unlike <see cref="ICommonServices"/> they are
/// not handed out to every loop: a scene services instance exposes them through
/// <c>Presentation</c>, and <see cref="ServiceScope.Require"/> keeps a call outside that scene loop's
/// Setup/Update/Shutdown (or after the scene was replaced) from touching the wrong scene.
/// </summary>
internal sealed class PresentationServices : IPresentationServices
{
    /// <summary>The instance every scene services object hands out as <c>Presentation</c>.</summary>
    internal static readonly PresentationServices Shared = new();

    /// <summary>Asset paths already reported by the effect/audio placeholders, so a per frame call cannot
    /// flood the host log.</summary>
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    public void ShakeCamera(float duration, float strength, float frequency)
    {
        ServiceScope.Require();
        // The game takes the strength first, then the duration, then the fade out, where the service takes
        // them (duration, strength, frequency).
        UniversalGameManager.SetCameraShake(strength, duration, frequency);
    }

    public IVfxHandle PlayVfx(string assetPath, Vector3 position)
    {
        ServiceScope.Require();
        Report("PlayVfx", assetPath);
        return NoopVfxHandle.Instance;
    }

    public void PlayAudio(string assetPath)
    {
        ServiceScope.Require();
        Report("PlayAudio", assetPath);
    }

    public Vector3 PlayerPosition
    {
        get
        {
            ServiceScope.Require();
            // The game's own player origin (the same call the game's spells make through SpellBase).
            return SpellBase.GetPlayerPosition();
        }
    }

    public Vector3 TablePosition(int deskCode)
    {
        ServiceScope.Require();
        // The game's own table target, i.e. what a spell aims its delivery at for that desk.
        return SpellBase.GetGuestTable(deskCode);
    }

    /// <summary>
    /// Effects and audio are still placeholders. Resolving an asset path works like <c>SpriteFiles</c> does it
    /// for sprites — an absolute path stands on its own and a relative one hangs off the mod's own directory —
    /// but this service is shared by every mod and never sees the caller's own mod directory,
    /// so the call is recorded once per path in the host log and nothing is instantiated.
    /// The API keeps its shape: the effect call still hands back a handle whose <c>Stop</c> is a no-op.
    /// </summary>
    private static void Report(string member, string assetPath)
    {
        if (!Reported.Add(member + " " + assetPath))
            return;
        GameBridgeHook.Trace(
            $"{member}: '{assetPath}' is not wired yet (no per-mod asset origin on ICommonServices); the call was ignored.");
    }

    private sealed class NoopVfxHandle : IVfxHandle
    {
        internal static readonly NoopVfxHandle Instance = new();

        public void Stop()
        {
        }
    }
}
