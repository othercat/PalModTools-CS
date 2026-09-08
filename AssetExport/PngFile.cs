// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace PalAssetExport;

/// <summary>Lossless PNG serialization over BCL zlib; no resizing, color keys, premultiplication or UI backend.</summary>
public static class PngFile
{
    public static void Write(string path, int width, int height, byte[] rgba)
    {
        if (width < 1 || height < 1 || rgba.Length != checked(width * height * 4)) throw new FormatException("Invalid RGBA dimensions.");
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        Span<byte> header = stackalloc byte[13]; header.Clear();
        BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.Slice(4), height);
        header[8] = 8; header[9] = 6; Chunk(output, "IHDR"u8, header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            for (var row = 0; row < height; row++) { zlib.WriteByte(0); zlib.Write(rgba.AsSpan(row * width * 4, width * 4)); }
        Chunk(output, "IDAT"u8, compressed.GetBuffer().AsSpan(0, checked((int)compressed.Length)));
        Chunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }
    private static void Chunk(Stream stream, ReadOnlySpan<byte> kind, ReadOnlySpan<byte> bytes)
    {
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, bytes.Length);
        stream.Write(number); stream.Write(kind); stream.Write(bytes);
        var crc = uint.MaxValue;
        foreach (var b in kind) crc = Table[(crc ^ b) & 255] ^ (crc >> 8);
        foreach (var b in bytes) crc = Table[(crc ^ b) & 255] ^ (crc >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); stream.Write(number);
    }
    private static readonly uint[] Table = MakeTable();
    private static uint[] MakeTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xedb88320U ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }
}
