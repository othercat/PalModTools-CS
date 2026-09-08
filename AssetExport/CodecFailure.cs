// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace SimpleUtility;

// The linked codecs use this host callback. A console import fails with an error,
// without loading the Common GUI message-box/TypeScript/Win32 helper graph.
public static class S
{
    public static void Failed(string operation, string message, bool success = false)
    {
        if (!success) throw new FormatException(operation + ": " + message);
    }
}
