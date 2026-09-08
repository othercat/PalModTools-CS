// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Globalization;
using System.Linq;

namespace PalAssetExport;

public static class FrameSelection
{
    public static int[]? Parse(string? text)
    {
        if (text is null) return null;
        if (text.Length is < 1 or > 1024) throw new FormatException("--frames requires a bounded comma-separated list of source frame IDs.");
        var tokens = text.Split(',');
        if (tokens.Length > 256) throw new FormatException("At most 256 source frames may be selected.");
        var frames = tokens.Select(token => int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 255
            ? value : throw new FormatException("--frames must contain source frame IDs from 0 to 255.")).ToArray();
        if (frames.Distinct().Count() != frames.Length) throw new FormatException("Repeated source frame ID.");
        Array.Sort(frames); return frames;
    }

    public static int[] Resolve(int[]? selected, int count)
    {
        if (selected is null) return Enumerable.Range(0, count).ToArray();
        if (selected.Any(frame => frame >= count)) throw new FormatException("Selected source frame does not exist.");
        return selected;
    }
}
