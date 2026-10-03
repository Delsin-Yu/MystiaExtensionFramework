using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace Mystia.Assets;

// The WAV half of the audio path: the engine only ever builds a clip out of floats, so a mod that ships a
// .wav file needs the container decoded first. The decode is pure managed arithmetic — RIFF chunk walking and
// sample scaling — which is why it lives in the SDK and can be exercised without the engine.
/// <summary>
/// The audio a RIFF/WAVE file carries, decoded into the interleaved float samples
/// <c>IAssetFactory.TryCreateAudioClip</c> takes. Channels are interleaved one frame at a time, and every
/// sample is in -1..1.
/// <para>
/// The decoder reads what mods actually ship: PCM at 8, 16, 24 or 32 bits and 32 bit IEEE float, mono or
/// multi channel, any sample rate, with the optional chunks a writer may put in front of the two that matter.
/// Anything else is refused rather than approximated — a file this decoder cannot read is reported as
/// <see langword="false"/>, never thrown.
/// </para>
/// </summary>
public sealed class WavAudio
{
    internal WavAudio(int channels, int sampleRate, float[] samples)
    {
        Channels = channels;
        SampleRate = sampleRate;
        Samples = samples;
    }

    /// <summary>The channels in one frame; at least one.</summary>
    public int Channels { get; }

    /// <summary>The frames per second.</summary>
    public int SampleRate { get; }

    /// <summary>The samples of every channel, one frame at a time (left, right, left, right, …).</summary>
    public float[] Samples { get; }

    /// <summary>The frames the file holds, one frame being one sample of every channel.</summary>
    public int Frames => Samples.Length / Channels;

    /// <summary>
    /// Decodes a RIFF/WAVE file. False when the bytes are not a WAVE file, when the format is not PCM or IEEE
    /// float, when the bit depth is not one this decoder scales, or when the file is truncated.
    /// </summary>
    /// <param name="wav">The bytes of the file.</param>
    /// <param name="audio">The decoded samples, or null when the file was refused.</param>
    public static bool TryDecode(ReadOnlySpan<byte> wav, [NotNullWhen(true)] out WavAudio? audio)
    {
        audio = null;

        // A header of 12 bytes and the two chunks that matter are 44 bytes at the very least.
        if (wav.Length < 44)
            return false;
        if (!wav.Slice(0, 4).SequenceEqual("RIFF"u8) || !wav.Slice(8, 4).SequenceEqual("WAVE"u8))
            return false;

        var format = 0;
        var channels = 0;
        var sampleRate = 0;
        var bits = 0;
        var hasFormat = false;
        var data = ReadOnlySpan<byte>.Empty;

        // Chunks are word aligned, so an odd sized chunk is followed by one pad byte.
        var offset = 12;
        while (offset + 8 <= wav.Length)
        {
            var id = wav.Slice(offset, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(wav.Slice(offset + 4, 4));
            var start = offset + 8;
            if (size < 0 || start + size > wav.Length)
                return false;

            if (id.SequenceEqual("fmt "u8))
            {
                // 16 bytes carry the format tag, the channel count, the sample rate and the bit depth.
                if (size < 16)
                    return false;
                var header = wav.Slice(start, size);
                format = BinaryPrimitives.ReadUInt16LittleEndian(header);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(14));
                hasFormat = true;
            }
            else if (id.SequenceEqual("data"u8))
            {
                data = wav.Slice(start, size);
                break;
            }

            offset = start + size + (size & 1);
        }

        if (!hasFormat || data.IsEmpty || channels == 0 || sampleRate <= 0)
            return false;

        // Format 1 is PCM, format 3 is IEEE float. WAVE_FORMAT_EXTENSIBLE (0xFFFE) describes the same two
        // layouts behind a sub format GUID and is not read here.
        var isFloat = format == 3;
        if (!isFloat && format != 1)
            return false;
        if (isFloat ? bits != 32 : bits is not (8 or 16 or 24 or 32))
            return false;

        var bytesPerSample = bits / 8;
        var frames = data.Length / bytesPerSample / channels;
        if (frames == 0)
            return false;

        // A frame that is cut short at the end of the file is dropped rather than played as a partial frame.
        var count = frames * channels;
        var samples = new float[count];
        if (isFloat)
        {
            for (var index = 0; index < count; index++)
                samples[index] = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(index * 4, 4));
        }
        else
        {
            switch (bits)
            {
                case 8:
                    // 8 bit PCM is unsigned and biased by 128.
                    for (var index = 0; index < count; index++)
                        samples[index] = (data[index] - 128) / 128f;
                    break;
                case 16:
                    for (var index = 0; index < count; index++)
                        samples[index] = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(index * 2, 2)) / 32768f;
                    break;
                case 24:
                    for (var index = 0; index < count; index++)
                    {
                        var at = index * 3;
                        var value = data[at] | (data[at + 1] << 8) | (data[at + 2] << 16);
                        if ((value & 0x800000) != 0)
                            value |= unchecked((int)0xFF000000); // sign extend
                        samples[index] = value / 8388608f;
                    }

                    break;
                default:
                    for (var index = 0; index < count; index++)
                        samples[index] = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(index * 4, 4)) / 2147483648f;
                    break;
            }
        }

        audio = new WavAudio(channels, sampleRate, samples);
        return true;
    }
}
