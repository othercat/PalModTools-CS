// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Globalization;
using System.Linq;

namespace PalAssetExport;

/// <summary>Optional indexed-color conversion before PAT lookup; never changes RLE coverage.</summary>
public sealed class PaletteTransform
{
    public int? ColorBank { get; }
    public int ShadeShift { get; }
    private PaletteTransform(int? bank, int shift) { ColorBank = bank; ShadeShift = shift; }

    public static PaletteTransform Parse(string? bank, string? shift)
    {
        if (bank is null)
        {
            if (shift is not null) throw new FormatException("--shade-shift requires --color-bank.");
            return new(null, 0);
        }
        if (!int.TryParse(bank, NumberStyles.None, CultureInfo.InvariantCulture, out var colorBank) || colorBank is < 0 or > 15)
            throw new FormatException("--color-bank must be an integer from 0 to 15.");
        var shadeShift = 0;
        if (shift is not null && (!int.TryParse(shift, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out shadeShift) || shadeShift is < -15 or > 15))
            throw new FormatException("--shade-shift must be an integer from -15 to 15.");
        return new(colorBank, shadeShift);
    }

    public byte MapIndex(byte index) => ColorBank is int bank
        ? (byte)((bank << 4) | Math.Clamp((index & 15) + ShadeShift, 0, 15)) : index;

    public byte[] ToRgba(RlePixels image, byte[] rgb6)
    {
        if (rgb6.Length != 768 || rgb6.Any(c => c > 63)) throw new FormatException("PAT is not a 256-entry 6-bit RGB palette.");
        var rgba = new byte[image.Indices.Length * 4];
        for (var j = 0; j < image.Indices.Length; j++)
        {
            if (image.Alpha[j] != 0)
            {
                var index = MapIndex(image.Indices[j]);
                for (var c = 0; c < 3; c++) rgba[j * 4 + c] = (byte)(rgb6[index * 3 + c] * 4);
            }
            rgba[j * 4 + 3] = image.Alpha[j];
        }
        return rgba;
    }
}
