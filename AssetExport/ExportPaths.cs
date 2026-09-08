// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;

namespace PalAssetExport;

public static class ExportPaths
{
    public static string Validate(string output, params string[] sources)
    {
        output = OrdinaryPath(output);
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Output must be a new directory.");
        foreach (var source in sources)
        {
            var parent = Path.GetDirectoryName(OrdinaryPath(source))!;
            var rel = Path.GetRelativePath(parent, output);
            if (rel == "." || (!Path.IsPathRooted(rel) && rel != ".." && !rel.StartsWith(".." + Path.DirectorySeparatorChar)))
                throw new IOException("Output must be outside the source resource directory.");
        }
        return output;
    }

    private static string OrdinaryPath(string path)
    {
        path = Path.GetFullPath(path);
        // Do not normalize an alternate path spelling into a source directory.
        // This initial exporter accepts ordinary paths, not device/8.3 aliases.
        if (OperatingSystem.IsWindows())
        {
            if (path.StartsWith(@"\\?\") || path.StartsWith(@"\\.\"))
                throw new IOException("Device paths are not supported for export.");
            foreach (var part in path[Path.GetPathRoot(path)!.Length..].Split(Path.DirectorySeparatorChar))
                if (part.Contains('~') || part.Contains(':') || part.EndsWith('.') || part.EndsWith(' '))
                    throw new IOException("Use ordinary long paths without alternate aliases.");
        }
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Resource and output paths must not traverse a link or junction.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return path;
    }
}
