// SPDX-License-Identifier: GPL-3.0-or-later
using Lib.Pal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PalAssetExport;

public static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception error) { Console.Error.WriteLine(error.GetType().Name + ": " + error.Message); return 1; }
    }
    private static int Run(string[] args)
    {
        if (args.Length == 0 || args.SequenceEqual(new[] { "--help" }))
        {
            Console.WriteLine("PalAssetExport --mkf FILE --chunk N --kind sprite|rle|bitmap320 --compression yj1|yj2|none --family ABC|F|MGO|FIRE|RGM|FBP|DATA --palette PAT.MKF --palette-chunk N --variant day|night --output NEW-DIRECTORY\nExports original-size RGBA PNGs and source hashes. Never writes source resources. No game initialization, Python, C++ library or network."); return 0;
        }
        var options = new Dictionary<string,string>(StringComparer.Ordinal);
        var allowed = new[] { "--mkf", "--chunk", "--kind", "--compression", "--family", "--palette", "--palette-chunk", "--variant", "--output" };
        for (var i = 0; i < args.Length; i += 2)
            if (i + 1 >= args.Length || !allowed.Contains(args[i]) || !options.TryAdd(args[i], args[i + 1])) throw new FormatException("Unknown, repeated or incomplete option.");
        foreach (var name in allowed) if (!options.ContainsKey(name)) throw new FormatException("Missing " + name);
        if (!int.TryParse(options["--chunk"], out var chunk) || !int.TryParse(options["--palette-chunk"], out var paletteChunk)) throw new FormatException("Chunk IDs must be integers.");
        var family = options["--family"]; var kind = options["--kind"]; var variant = options["--variant"];
        if (!new[] { "ABC", "F", "MGO", "FIRE", "RGM", "FBP", "DATA" }.Contains(family) || !new[] { "sprite", "rle", "bitmap320" }.Contains(kind) || variant is not ("day" or "night"))
            throw new FormatException("Choose a supported family, image kind and palette variant explicitly.");
        var sourcePaths = new[] { options["--mkf"], options["--palette"] };
        var output = ExportPaths.Validate(options["--output"], sourcePaths);
        var sourceInput = ResourceInput.Read(options["--mkf"], chunk);
        var palette = ResourceInput.Read(options["--palette"], paletteChunk);
        if (palette.Bytes.Length is not (768 or 1536) || (variant == "night" && palette.Bytes.Length != 1536)) throw new FormatException("Selected PAT chunk/variant is not present.");
        var rgb6 = palette.Bytes.AsSpan(variant == "night" ? 768 : 0, 768).ToArray();
        if (rgb6.Any(c => c > 63)) throw new FormatException("PAT is not a 6-bit RGB palette.");
        var decoded = sourceInput.Decode(options["--compression"]);
        var frames = kind == "sprite" ? ResourceInput.SpriteFrames(decoded) : new[] { decoded };
        var images = new List<RlePixels>(); var remainingPixels = 33554432;
        foreach (var bytes in frames)
        {
            var image = kind == "bitmap320" ? Bitmap(bytes) : RlePixels.Read(bytes, remainingPixels);
            remainingPixels -= image.Indices.Length;
            if (remainingPixels < 0) throw new FormatException("Frame group exceeds the 32 Mi pixel budget.");
            images.Add(image);
        }
        ExportPaths.Validate(output, sourcePaths);
        var staging = output + ".partial-" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(staging);
        var records = new List<object>();
        for (var i = 0; i < images.Count; i++)
        {
            var image = images[i]; var rgba = new byte[image.Indices.Length * 4];
            for (var j = 0; j < image.Indices.Length; j++)
            {
                if (image.Alpha[j] != 0) for (var c = 0; c < 3; c++) rgba[j * 4 + c] = (byte)(rgb6[image.Indices[j] * 3 + c] * 4);
                rgba[j * 4 + 3] = image.Alpha[j];
            }
            var name = i.ToString("D5") + ".png";
            PngFile.Write(Path.Combine(staging, name), image.Width, image.Height, rgba);
            records.Add(new { frame = i, file = name, width = image.Width, height = image.Height, sha256 = ResourceInput.FileHash(Path.Combine(staging, name)), rgba_sha256 = ResourceInput.Hash(rgba), rle_consumed_bytes = image.ConsumedBytes,
                transparent_pixels = image.Alpha.Count(a => a == 0), opaque_index0 = image.Indices.Where((v, j) => v == 0 && image.Alpha[j] != 0).Count(), opaque_index255 = image.Indices.Where((v, j) => v == 255 && image.Alpha[j] != 0).Count() });
        }
        if (ResourceInput.FileHash(sourceInput.Path) != sourceInput.Sha256 || ResourceInput.FileHash(palette.Path) != palette.Sha256) throw new IOException("Source changed during export; partial output retained.");
        var receipt = new { schema = "palmod.image-export.v1", family, kind, compression = options["--compression"],
            source = new { file = Path.GetFileName(sourceInput.Path), sha256 = sourceInput.Sha256, chunk, chunk_sha256 = ResourceInput.Hash(sourceInput.Bytes), decoded_sha256 = ResourceInput.Hash(decoded), decoded_bytes = decoded.Length },
            palette = new { file = Path.GetFileName(palette.Path), sha256 = palette.Sha256, chunk = paletteChunk, variant, conversion = "rgb6-times-four", selected_rgb6_sha256 = ResourceInput.Hash(rgb6) },
            exporter = new { implementation = "PalModTools-CS AssetExport", decoder_assembly_sha256 = ResourceInput.FileHash(typeof(PalUtil).Assembly.Location), codecs = "linked-owner-managed-MKF-YJ1-YJ2-RLE" },
            transparency = "rle-skip-coverage; literal-indices-0-and-255-remain-opaque", action_mapping = "not-inferred", frames = records };
        File.WriteAllText(Path.Combine(staging, "export.json"), JsonSerializer.Serialize(receipt, Json) + "\n");
        ExportPaths.Validate(output, sourcePaths);
        Directory.Move(staging, output);
        Console.WriteLine(JsonSerializer.Serialize(new { success = true, output_directory = output, frame_count = images.Count, receipt_sha256 = ResourceInput.FileHash(Path.Combine(output, "export.json")) }, Json)); return 0;
    }
    private static RlePixels Bitmap(byte[] bytes)
    {
        if (bytes.Length != 320 * 200) throw new FormatException("bitmap320 requires exactly 64000 decoded pixels.");
        var alpha = new byte[bytes.Length]; Array.Fill(alpha, (byte)255); return new(320, 200, bytes, alpha, bytes.Length);
    }
}
