// SPDX-License-Identifier: GPL-3.0-or-later
using Lib.Pal;
using SimpleUtility;
using System;
using System.Buffers.Binary;

namespace PalAssetExport;

/// <summary>Bound the input before calling the existing decoder; coverage is not a palette color key.</summary>
public sealed record RlePixels(int Width, int Height, byte[] Indices, byte[] Alpha, int ConsumedBytes)
{
    public static unsafe RlePixels Read(ReadOnlySpan<byte> source, int pixelBudget = 33554432)
    {
        var cursor = source.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(source) == 2 ? 4 : 0;
        if (source.Length - cursor < 4) throw new FormatException("Truncated RLE header.");
        var width = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(cursor, 2));
        var height = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(cursor + 2, 2));
        if (width is < 1 or > 8192 || height is < 1 or > 8192 || (long)width * height > Math.Min(pixelBudget, 33554432))
            throw new FormatException("RLE dimensions exceed the image budget.");
        cursor += 4;
        var pixels = checked(width * height); var advanced = 0;
        while (advanced < pixels)
        {
            if (cursor >= source.Length) throw new FormatException("Truncated RLE command.");
            var code = source[cursor++];
            var skip = (code & 128) != 0 && code <= 128 + width;
            var count = skip ? code - 128 : code;
            if (count > pixels - advanced) throw new FormatException("RLE run exceeds the declared image.");
            if (!skip)
            {
                if (count > source.Length - cursor) throw new FormatException("Truncated RLE literal run.");
                cursor += count;
            }
            advanced += count;
        }
        byte[] zero, white;
        nint a = 0, b = 0;
        try
        {
            fixed (byte* src = source)
            {
                int sizeA, sizeB;
                (a, sizeA) = PalUtil.UnpackRle((nint)src, 0);
                (b, sizeB) = PalUtil.UnpackRle((nint)src, 255);
                if (a == 0 || b == 0 || sizeA != pixels || sizeB != pixels)
                    throw new FormatException("RLE decoder returned inconsistent dimensions.");
            }
            zero = new ReadOnlySpan<byte>((void*)a, pixels).ToArray();
            white = new ReadOnlySpan<byte>((void*)b, pixels).ToArray();
        }
        finally { C.free(a); C.free(b); }
        var alpha = new byte[pixels];
        for (var i = 0; i < pixels; i++)
        {
            if (zero[i] == white[i]) alpha[i] = 255;
            else if (zero[i] != 0 || white[i] != 255) throw new FormatException("Decoder coverage is inconsistent.");
        }
        return new(width, height, zero, alpha, cursor);
    }
}
