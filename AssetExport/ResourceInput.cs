// SPDX-License-Identifier: GPL-3.0-or-later
using Lib.Pal;
using SimpleUtility;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace PalAssetExport;

public sealed record ResourceInput(string Path, string Sha256, int Chunk, byte[] Bytes)
{
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string FileHash(string path)
    {
        using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    public static unsafe ResourceInput Read(string path, int chunk)
    {
        path = System.IO.Path.GetFullPath(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        {
            if (stream.Length is < 8 or > 536870912) throw new FormatException("MKF size is outside the 512 MiB budget.");
            Span<byte> word = stackalloc byte[4]; stream.ReadExactly(word);
            var first = BinaryPrimitives.ReadUInt32LittleEndian(word);
            if (first < 8 || first > 262148 || first % 4 != 0 || first > stream.Length) throw new FormatException("Invalid MKF table.");
            var count = checked((int)first / 4 - 1);
            if (chunk < 0 || chunk >= count) throw new FormatException("Chunk is outside the MKF table.");
            uint previous = first;
            for (var i = 1; i <= count; i++)
            {
                stream.ReadExactly(word); var next = BinaryPrimitives.ReadUInt32LittleEndian(word);
                if (next < previous || next > stream.Length) throw new FormatException("Invalid MKF offset ordering/range.");
                previous = next;
            }
        }
        var hash = FileHash(path);
        using var reader = new MkfReader(path);
        if (reader.GetChunkSize(chunk) is < 1 or > 67108864) throw new FormatException("Empty or oversized resource chunk.");
        var (ptr, size) = reader.ReadChunk(chunk);
        try
        {
            var bytes = new ReadOnlySpan<byte>((void*)ptr, size).ToArray();
            if (FileHash(path) != hash) throw new IOException("Source changed while reading.");
            return new(path, hash, chunk, bytes);
        }
        finally { C.free(ptr); }
    }

    public unsafe byte[] Decode(string compression)
    {
        if (compression == "none") return Bytes;
        var dos = compression == "yj1";
        if (!dos && compression != "yj2") throw new FormatException("Choose yj1, yj2 or none explicitly.");
        if (Bytes.Length < (dos ? 16 : 4) || (dos && !Bytes.AsSpan(0, 4).SequenceEqual("YJ_1"u8)))
            throw new FormatException("Compressed resource header does not match the selected format.");
        var expected = BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(dos ? 4 : 0, 4));
        if (expected is < 1 or > 67108864) throw new FormatException("Decoded resource exceeds the 64 MiB budget.");
        fixed (byte* input = Bytes)
        {
            var (ptr, size) = PalUtil.Unpack((nint)input, Bytes.Length, dos, 67108864);
            try
            {
                if (ptr == 0 || size != expected) throw new FormatException("Decoded size does not match the header.");
                return new ReadOnlySpan<byte>((void*)ptr, size).ToArray();
            }
            finally { C.free(ptr); }
        }
    }

    public static IReadOnlyList<byte[]> SpriteFrames(byte[] source)
    {
        if (source.Length < 4) throw new FormatException("Truncated sprite table.");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(source);
        if (count is < 1 or > 257 || count * 2 > source.Length) throw new FormatException("Invalid sprite table length.");
        // Some DATA #9 groups end with a zero table entry. It terminates the table,
        // not the preceding image; internal zeros still fail the normal range checks.
        var zeroSentinel = count > 1 && BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan((count - 1) * 2, 2)) == 0;
        var frameCount = zeroSentinel ? count - 1 : count;
        var result = new List<byte[]>();
        for (var i = 0; i < frameCount; i++)
        {
            var start = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(i * 2, 2)) * 2;
            var end = i + 1 < frameCount ? BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan((i + 1) * 2, 2)) * 2 : source.Length;
            if (!zeroSentinel && i == count - 1 && start == source.Length) break; // An actual terminal offset, not an assumed extra frame.
            if (start < count * 2 || start >= source.Length || end <= start || end > source.Length)
                throw new FormatException("Unsupported or invalid sprite frame offsets.");
            if (result.Count == 256) throw new FormatException("Sprite exceeds the 256-frame import budget.");
            result.Add(source.AsSpan(start, end - start).ToArray());
        }
        if (result.Count == 0) throw new FormatException("Sprite contains no image frames.");
        return result;
    }
}
