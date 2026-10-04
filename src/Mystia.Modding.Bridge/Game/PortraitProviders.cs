using Common.UI.NoteBookUtility;
using GameData.Core.Collections.CharacterUtility;
using GameData.Profile;
using GameData.RunTime.Common;
using HarmonyLib;
using Mystia.Listeners;
using UnityEngine;
using UnityEngine.UI;

namespace Mystia.Modding.Bridge;

/// <summary>
/// Runtime portrait chain for clothes.
/// <para>
/// <c>OnInjectClothes</c> only carries relative paths, so an injected garment has no addressable
/// visual: the declaration of the portrait (and any animated variant) has to come from the mod. The
/// bridge asks every <see cref="IPortraitProvider"/> in mod order before the game loads its own
/// clothes visual and writes the hit onto <c>Image.overrideSprite</c>. The original method still runs
/// (the prefix never cancels it), so the stock visuals plus the stock animated portrait coroutine stay
/// intact for garments no provider resolves.
/// </para>
/// </summary>
internal static class PortraitProviders
{
    /// <summary>
    /// The index of the clothes the game currently has loaded. <c>DataBaseCharacter.SetupPortrayalVisual</c> reads
    /// its own field first, so that field is authoritative; <c>RunTimeAlbum.GetPlayerClothes</c> covers
    /// the window before the skin was loaded. Negative when there are no clothes to ask about.
    /// </summary>
    internal static int CurrentClothIndex()
    {
        ClothesProfile.Clothes? clothes;
        if (DataBaseCharacter.m_ClothesData is { } loaded)
            clothes = loaded;
        else
        {
            try
            {
                clothes = RunTimeAlbum.GetPlayerClothes();
            }
            catch (Exception error)
            {
                GameBridgeHook.Trace($"PortraitProviders: cannot resolve the player clothes: {error.GetBaseException().Message}");
                clothes = null;
            }
        }

        return clothes?.index ?? -1;
    }

    /// <summary>
    /// First provider that answers for the garment on that panel wins; failures never disturb the pipeline. A
    /// provider answers in handles, so the sprite the panel draws is the one this framework built or wrapped.
    /// </summary>
    internal static Sprite? Resolve(int clothIndex, PortraitTarget target)
    {
        if (clothIndex < 0)
            return null;
        foreach (var provider in Dispatch.Instances<IPortraitProvider>())
        {
            try
            {
                if (!provider.TryResolvePortrait(clothIndex, target, out var portrait))
                    continue;
                if (portrait is not UnitySpriteHandle { Sprite: not null } resolved)
                {
                    GameBridgeHook.Trace($"PortraitProviders: {provider.GetType().FullName} reported a hit without a sprite");
                    continue;
                }

                return resolved.Sprite;
            }
            catch (Exception error)
            {
                GameBridgeHook.Trace($"PortraitProviders: {provider.GetType().FullName} failed: {error.GetBaseException().Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// Which panel is setting the portrait up. The one method every panel draws through is handed the panel as
    /// its coroutine runner, so the notebook is recognised by that component and everything else is the HUD case
    /// the game hands the player's portrait to alike.
    /// </summary>
    internal static PortraitTarget TargetOf(MonoBehaviour? coroutineRunner) =>
        coroutineRunner is NoteBookProfilePannel ? PortraitTarget.NoteBook : PortraitTarget.Hud;
}

// Named apart from PortraitSprites' PortraitSeams (the four CharacterPortrayal load seams).
//
// SetupPortrayalVisual is the one panel facing portrait entry point there is, so a provider covers every panel
// that draws the player's portrait: the day HUD (UIManager.cs:193) and the note book's profile page
// (NoteBookProfilePannel.cs:72, which hands in its mystiaPic) reach the same call, and the prefix below writes
// the provider's sprite onto the Image it was given. The sprite lands on overrideSprite, the sprite UGUI draws
// and SetNativeSize measures, which also covers the animated clothes portrait those panels start. The note book
// therefore needs no seam of its own, and no second provider interface either: the panel is asked for the same
// clothes the day HUD is.
internal static class PortraitProviderSeams
{
    [HarmonyPatch(typeof(DataBaseCharacter), nameof(DataBaseCharacter.SetupPortrayalVisual))]
    private static class Setup
    {
        // Prefix, never skipping: the original call sets the stock sprite, starts (or restarts) the
        // animated portrait coroutine and reports whether any visual exists, and overrideSprite keeps
        // winning over the frames that coroutine writes into Image.sprite.
        private static void Prefix(Image imageComponent, MonoBehaviour coroutineRunner)
        {
            if (imageComponent is null)
                return;
            var sprite = PortraitProviders.Resolve(
                PortraitProviders.CurrentClothIndex(),
                PortraitProviders.TargetOf(coroutineRunner));
            if (sprite is not null)
                imageComponent.overrideSprite = sprite;
        }
    }
}
