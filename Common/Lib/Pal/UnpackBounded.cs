// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace Lib.Pal;

public static unsafe partial class PalUtil
{
    /// <summary>Decode a complete MKF chunk with explicit input and output bounds.</summary>
    public static (nint buffer, int bufferSize) Unpack(nint source, int sourceLength, bool isDosGame, int outputLimit)
    {
        if (sourceLength < 0 || outputLimit < 1) throw new ArgumentOutOfRangeException(nameof(sourceLength));
        return isDosGame ? UnpackDos(source, sourceLength, outputLimit) : UnpackWin(source, sourceLength, outputLimit);
    }
}
