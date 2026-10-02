using System.IO.Compression;
using UnityEngine;

namespace Mystia.Modding.Bridge;

internal static class SpriteFiles
{
    private static readonly Dictionary<string, Sprite> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Sprite Load(string modDirectory, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A sprite path is required.", nameof(path));
        var full = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(modDirectory, path));
        if (Cache.TryGetValue(full, out var cached) && cached != null)
            return cached;
        if (!File.Exists(full))
            throw new FileNotFoundException("Sprite file was not found.", full);

        var pixels = ReadPng(File.ReadAllBytes(full));
        var texture = new Texture2D(pixels.Width, pixels.Height, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels.Pixels);
        texture.Apply(false, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.hideFlags = HideFlags.HideAndDontSave;
        var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 48f);
        sprite.name = Path.GetFileNameWithoutExtension(full);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        Cache[full] = sprite;
        return sprite;
    }

    private static Png ReadPng(byte[] data)
    {
        if (data.Length < 8 || data[0] != 137 || data[1] != 80 || data[2] != 78 || data[3] != 71)
            throw new InvalidDataException("Only PNG images can be loaded.");
        var width = 0;
        var height = 0;
        var colorType = 0;
        using var compressed = new MemoryStream();
        var offset = 8;
        while (offset + 8 <= data.Length)
        {
            var length = ReadInt(data, offset);
            var kind = System.Text.Encoding.ASCII.GetString(data, offset + 4, 4);
            var start = offset + 8;
            if (kind == "IHDR")
            {
                width = ReadInt(data, start);
                height = ReadInt(data, start + 4);
                if (data[start + 8] != 8 || data[start + 12] != 0 || (data[start + 9] != 2 && data[start + 9] != 6))
                    throw new InvalidDataException("PNG images must be 8-bit RGB or RGBA without interlacing.");
                colorType = data[start + 9];
            }
            else if (kind == "IDAT")
            {
                compressed.Write(data, start, length);
            }
            else if (kind == "IEND")
            {
                break;
            }

            offset = start + length + 4;
        }

        if (width <= 0 || height <= 0)
            throw new InvalidDataException("PNG image has no header.");
        var channels = colorType == 6 ? 4 : 3;
        compressed.Position = 2;
        using var deflate = new DeflateStream(compressed, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        deflate.CopyTo(raw);
        var bytes = raw.ToArray();
        var stride = width * channels;
        var pixels = new Color32[width * height];
        var previous = new byte[stride];
        var cursor = 0;
        for (var y = 0; y < height; y++)
        {
            var filter = bytes[cursor++];
            var row = new byte[stride];
            for (var x = 0; x < stride; x++)
            {
                var value = bytes[cursor++];
                var left = x >= channels ? row[x - channels] : (byte)0;
                var up = previous[x];
                var upLeft = x >= channels ? previous[x - channels] : (byte)0;
                row[x] = filter switch
                {
                    0 => value,
                    1 => (byte)(value + left),
                    2 => (byte)(value + up),
                    3 => (byte)(value + ((left + up) / 2)),
                    4 => (byte)(value + Paeth(left, up, upLeft)),
                    _ => throw new InvalidDataException("PNG row filter is not supported."),
                };
            }

            var targetRow = height - 1 - y;
            for (var x = 0; x < width; x++)
            {
                var source = x * channels;
                pixels[(targetRow * width) + x] = new Color32(row[source], row[source + 1], row[source + 2], channels == 4 ? row[source + 3] : (byte)255);
            }

            previous = row;
        }

        return new Png(width, height, pixels);
    }

    private static int ReadInt(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static byte Paeth(byte left, byte up, byte upLeft)
    {
        var estimate = left + up - upLeft;
        var leftDistance = Math.Abs(estimate - left);
        var upDistance = Math.Abs(estimate - up);
        var diagonal = Math.Abs(estimate - upLeft);
        if (leftDistance <= upDistance && leftDistance <= diagonal)
            return left;
        return upDistance <= diagonal ? up : upLeft;
    }

    private readonly struct Png
    {
        internal Png(int width, int height, Color32[] pixels)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        internal int Width { get; }

        internal int Height { get; }

        internal Color32[] Pixels { get; }
    }
}
