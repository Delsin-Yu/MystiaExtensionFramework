using UnityEngine;

namespace Mystia.Modding.Bridge;

// The framework's own path from a mod's file to an engine sprite, for the game records that take a sprite rather
// than a handle (a special guest's portraits and pixel art, a language entry's picture). It goes through the
// asset factory, so a file is decoded by the same decoder a mod reaches through IAssetFactory - the one that
// reads what PNG allows - and the texture it builds is the factory's own pixel art texture.
internal static class SpriteFiles
{
    private static readonly Dictionary<string, Sprite> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The sprite of one file, cached per path: the game records ask for the same portrait once per guest, once
    /// per spell and once when a notebook page opens.
    /// <para>
    /// A path that names no readable picture is an error rather than an empty answer, because the game's own
    /// records do not carry one: the caller is a declaration a mod wrote, and a missing frame would show up as a
    /// guest drawn from nothing.
    /// </para>
    /// </summary>
    public static Sprite Load(string modDirectory, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A sprite path is required.", nameof(path));
        var full = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(modDirectory, path));
        if (Cache.TryGetValue(full, out var cached) && cached != null)
            return cached;
        if (!File.Exists(full))
            throw new FileNotFoundException("Sprite file was not found.", full);

        var factory = UnityAssetFactory.Shared;
        if (!factory.TryCreateTexture(File.ReadAllBytes(full), out var texture)
            || !factory.TryGetTextureSize(texture, out var width, out var height)
            || !factory.TryCreateSprite(
                texture,
                new Mystia.Numerics.Rect(0f, 0f, width, height),
                new Mystia.Numerics.Vector2(0.5f, 0.5f),
                48f,
                out var handle)
            || handle is not UnitySpriteHandle { Sprite: not null } sprite)
        {
            throw new InvalidDataException($"The image {full} is not a picture this framework reads.");
        }

        // The name a log or a debugger shows is the file's own, not the factory's texture name.
        sprite.Sprite.name = Path.GetFileNameWithoutExtension(full);
        Cache[full] = sprite.Sprite;
        return sprite.Sprite;
    }
}
