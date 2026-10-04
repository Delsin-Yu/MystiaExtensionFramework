namespace Mystia.Assets;

// The CRC the PNG chunks are built out of, shared by the decoder that checks it and the writer that produces
// it so that the two can never disagree about the polynomial. It is the reflected 0xEDB88320 form zlib uses,
// the same one that answers 0xCBF43926 for the bytes "123456789".
/// <summary>
/// The CRC-32 a PNG chunk carries: the reflected 0xEDB88320 polynomial, computed over the chunk's type and its
/// payload together.
/// </summary>
internal static class PngCrc32
{
    private static readonly uint[] Table = Build();

    /// <summary>Computes the CRC-32 of one run of bytes, or of two that are read as if they were joined.</summary>
    /// <param name="first">The bytes to run the CRC over.</param>
    /// <param name="second">The bytes that follow them; empty when there are none.</param>
    internal static uint Compute(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second = default)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in first)
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        foreach (var value in second)
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    // One entry per byte of input: the table is built once, the first time a chunk is checked.
    private static uint[] Build()
    {
        const uint polynomial = 0xEDB88320u;
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var value = n;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? polynomial ^ (value >> 1) : value >> 1;
            table[n] = value;
        }

        return table;
    }
}
