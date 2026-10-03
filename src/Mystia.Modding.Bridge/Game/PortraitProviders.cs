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
    /// The clothes the game currently has loaded. <c>DataBaseCharacter.SetupPortrayalVisual</c> reads
    /// its own field first, so that field is authoritative; <c>RunTimeAlbum.GetPlayerClothes</c> covers
    /// the window before the skin was loaded.
    /// </summary>
    internal static ClothesProfile.Clothes? CurrentClothes()
    {
        if (DataBaseCharacter.m_ClothesData is { } loaded)
            return loaded;
        try
        {
            return RunTimeAlbum.GetPlayerClothes();
        }
        catch (Exception error)
        {
            GameBridgeHook.Trace($"PortraitProviders: cannot resolve the player clothes: {error.GetBaseException().Message}");
            return null;
        }
    }

    /// <summary>First provider that resolves the clothes wins; failures never disturb the pipeline.</summary>
    internal static Sprite? Resolve(ClothesProfile.Clothes? clothes)
    {
        if (clothes is null)
            return null;
        foreach (var provider in Dispatch.Instances<IPortraitProvider>())
        {
            try
            {
                if (!provider.TryResolvePortrait(clothes, out var sprite))
                    continue;
                if (sprite is null)
                {
                    GameBridgeHook.Trace($"PortraitProviders: {provider.GetType().FullName} reported a hit without a sprite");
                    continue;
                }

                return sprite;
            }
            catch (Exception error)
            {
                GameBridgeHook.Trace($"PortraitProviders: {provider.GetType().FullName} failed: {error.GetBaseException().Message}");
            }
        }

        return null;
    }
}

// Named apart from PortraitSprites' PortraitSeams (the four CharacterPortrayal load seams).
internal static class PortraitProviderSeams
{
    [HarmonyPatch(typeof(DataBaseCharacter), nameof(DataBaseCharacter.SetupPortrayalVisual))]
    private static class Setup
    {
        // Prefix, never skipping: the original call sets the stock sprite, starts (or restarts) the
        // animated portrait coroutine and reports whether any visual exists, and overrideSprite keeps
        // winning over the frames that coroutine writes into Image.sprite.
        private static void Prefix(Image imageComponent)
        {
            if (imageComponent is null)
                return;
            var sprite = PortraitProviders.Resolve(PortraitProviders.CurrentClothes());
            if (sprite is not null)
                imageComponent.overrideSprite = sprite;
        }
    }
}
