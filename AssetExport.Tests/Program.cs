// SPDX-License-Identifier: GPL-3.0-or-later
using Lib.Pal;
using PalAssetExport;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

var output = Path.GetFullPath(args[0]);
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Test output must be new.");
Directory.CreateDirectory(output);
var checks = new List<object>(); var failures = 0;
void Check(string name, Action check)
{
    try { check(); checks.Add(new { name, passed = true, error = "" }); }
    catch (Exception error) { failures++; checks.Add(new { name, passed = false, error = error.ToString() }); }
}
void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
void Reject(Action action)
{
    try { action(); } catch (FormatException) { return; }
    throw new Exception("Malformed resource was accepted.");
}
byte[] sample = [4, 0, 1, 0, 0x81, 3, 0, 255, 23, 0];
Check("RLE skip differs from opaque palette indices 0 and 255", () =>
{
    var image = RlePixels.Read(sample);
    Equal("0,255,255,255", string.Join(",", image.Alpha)); Equal("0,0,255,23", string.Join(",", image.Indices)); Equal(9, image.ConsumedBytes);
});
Check("Optional DWORD prefix preserves all decoded pixels", () =>
{
    var image = RlePixels.Read(new byte[] { 2, 0, 0, 0 }.Concat(sample).ToArray());
    Equal("0,255,255,255", string.Join(",", image.Alpha)); Equal(13, image.ConsumedBytes);
});
Check("Literal run crosses a row without losing palette identity", () =>
{
    var image = RlePixels.Read(new byte[] { 2, 0, 2, 0, 4, 0, 255, 17, 18 });
    Equal("0,255,17,18", string.Join(",", image.Indices)); Equal(4, image.Alpha.Count(a => a == 255));
});
foreach (var item in new (string, byte[])[]
{
    ("missing header", []), ("zero dimensions", [0,0,1,0]), ("missing run", [1,0,1,0]),
    ("literal truncation", [2,0,1,0,2,3]), ("run beyond image", [1,0,1,0,2,3,4]),
    ("truncated optional header", [2,0,0,0,1]), ("nonprogress ends at input bound", [1,0,1,0,0x80,0]),
}) Check("Reject RLE " + item.Item1, () => Reject(() => RlePixels.Read(item.Item2)));
Check("Group pixel budget is enforced before decoder allocation", () => Reject(() => RlePixels.Read(sample, 3)));
byte[] Sprite(bool sentinel)
{
    var words = sentinel ? 3 : 2; var bytes = new byte[words * 2 + sample.Length * 2];
    BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)words);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), (ushort)(words + sample.Length / 2));
    if (sentinel) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), (ushort)(bytes.Length / 2));
    sample.CopyTo(bytes, words * 2); sample.CopyTo(bytes, words * 2 + sample.Length); return bytes;
}
Check("No-sentinel table retains the final real frame", () => Equal(2, ResourceInput.SpriteFrames(Sprite(false)).Count));
Check("Only an actual end offset is treated as a sentinel", () => Equal(2, ResourceInput.SpriteFrames(Sprite(true)).Count));
byte[] ZeroTail(int images)
{
    var words = images + 1; var data = new byte[words * 2 + images * sample.Length];
    for (var i = 0; i < images; i++)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * 2), (ushort)(words + i * sample.Length / 2));
        sample.CopyTo(data, words * 2 + i * sample.Length);
    }
    return data;
}
Check("Trailing zero keeps all 111 real source frames including frame 110", () =>
{
    var frames = ResourceInput.SpriteFrames(ZeroTail(111)); Equal(111, frames.Count);
    Equal(ResourceInput.Hash(sample), ResourceInput.Hash(frames[110])); Equal(9, RlePixels.Read(frames[110]).ConsumedBytes);
});
Check("One real image followed by zero is retained", () => Equal(1, ResourceInput.SpriteFrames(ZeroTail(1)).Count));
Check("Zero tail cannot hide truncated final RLE", () => Reject(() => RlePixels.Read(ResourceInput.SpriteFrames(ZeroTail(2)[..^2])[1])));
foreach (var defect in new[] { "internal-zero", "two-trailing-zeros", "end-then-zero", "descending-before-zero", "outside-before-zero", "sentinel-only" })
    Check("Reject sprite zero sentinel " + defect, () =>
    {
        var bytes = ZeroTail(3);
        if (defect == "sentinel-only") bytes = [2, 0, 0, 0];
        else BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(defect == "internal-zero" ? 2 : 4), defect switch
        {
            "internal-zero" or "two-trailing-zeros" => 0,
            "end-then-zero" => (ushort)(bytes.Length / 2),
            "descending-before-zero" => 4,
            _ => 65535,
        });
        Reject(() => ResourceInput.SpriteFrames(bytes));
    });
Check("Zero followed by nonzero is not a sentinel", () =>
{
    var bytes = Sprite(true); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 0);
    Reject(() => ResourceInput.SpriteFrames(bytes));
});
Check("Default palette conversion retains every index", () =>
{
    var transform = PaletteTransform.Parse(null, null);
    for (var index = 0; index < 256; index++) Equal((byte)index, transform.MapIndex((byte)index));
});
Check("Palette bank replaces high nibble and clamps shade both ways", () =>
{
    Equal((byte)0x10, PaletteTransform.Parse("1", "-4").MapIndex(0xF2));
    Equal((byte)0x2F, PaletteTransform.Parse("2", "+4").MapIndex(0xAF));
    Equal((byte)0x0B, PaletteTransform.Parse("0", "-4").MapIndex(0xFF));
    Equal((byte)0xFF, PaletteTransform.Parse("15", null).MapIndex(0x0F));
});
var testPalette = Enumerable.Range(0, 768).Select(i => (byte)(i % 64)).ToArray();
Check("Indexed transform preserves skip coverage and opaque literal zero and 255", () =>
{
    var image = RlePixels.Read(sample); var indices = image.Indices.ToArray(); var alpha = image.Alpha.ToArray();
    var rgba = PaletteTransform.Parse("1", "-4").ToRgba(image, testPalette);
    Equal("0,255,255,255", string.Join(",", Enumerable.Range(0, 4).Select(i => rgba[i * 4 + 3])));
    Equal("0,0,0,0", string.Join(",", rgba[..4]));
    Equal((byte)(testPalette[0x10 * 3] * 4), rgba[4]); Equal((byte)(testPalette[0x1B * 3] * 4), rgba[8]);
    Equal(true, indices.SequenceEqual(image.Indices)); Equal(true, alpha.SequenceEqual(image.Alpha));
});
foreach (var (bank, shift) in new (string?, string?)[] { (null, "0"), ("-1", null), ("16", null), ("0x10", null), ("", null), ("0", "-16"), ("0", "16"), ("0", "1.5"), ("0", "") })
    Check($"Reject palette options bank={bank ?? "absent"} shift={shift ?? "absent"}", () => Reject(() => PaletteTransform.Parse(bank, shift)));
Check("Explicit frames keep source order without renumbering", () => Equal("18,40,110", string.Join(",", FrameSelection.Resolve(FrameSelection.Parse("110,40,18"), 111))));
Check("Absent selection exports every real source frame", () => Equal("0,1", string.Join(",", FrameSelection.Resolve(FrameSelection.Parse(null), 2))));
foreach (var selection in new[] { "", "0,0", "-1", "256", "1,", "0,,1", "1-3", " 1", "1.0", "+1" })
    Check("Reject frame selection " + selection, () => Reject(() => FrameSelection.Parse(selection)));
Check("A missing selected frame is rejected", () => Reject(() => FrameSelection.Resolve(FrameSelection.Parse("111"), 111)));
foreach (var defect in new[] { "header", "inside-table", "descending", "past-end" })
    Check("Reject sprite " + defect, () =>
    {
        var bytes = Sprite(false);
        if (defect == "header") bytes = [4];
        else if (defect == "inside-table") BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 1);
        else if (defect == "descending") BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 2);
        else BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 65535);
        Reject(() => ResourceInput.SpriteFrames(bytes));
    });
byte[] Container()
{
    var data = new byte[8 + sample.Length]; BinaryPrimitives.WriteUInt32LittleEndian(data, 8);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)data.Length); sample.CopyTo(data, 8); return data;
}
var source = Path.Combine(output, "source.mkf"); File.WriteAllBytes(source, Container());
Check("Existing MKF reader extracts exact bounded source bytes", () =>
{
    var value = ResourceInput.Read(source, 0); Equal(ResourceInput.Hash(sample), ResourceInput.Hash(value.Bytes));
    Equal(ResourceInput.FileHash(source), value.Sha256);
});
Check("Negative MKF chunk is rejected", () => Reject(() => ResourceInput.Read(source, -1)));
Check("Missing MKF chunk is rejected", () => Reject(() => ResourceInput.Read(source, 1)));
foreach (var defect in new[] { "unaligned", "backwards", "past-end" })
    Check("Reject MKF " + defect, () =>
    {
        var data = Container(); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(defect == "unaligned" ? 0 : 4), defect switch { "unaligned" => 9u, "backwards" => 4u, _ => 65535u });
        var path = Path.Combine(output, defect + ".mkf"); File.WriteAllBytes(path, data); Reject(() => ResourceInput.Read(path, 0));
    });
Check("Compressed output budget rejects before entering existing unsafe codec", () =>
{
    var data = new byte[16]; "YJ_1"u8.CopyTo(data); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 67108865);
    Reject(() => new ResourceInput("fixture", "", 0, data).Decode("yj1"));
});
Check("Explicit compression selector rejects unknown and mismatched headers", () =>
{
    var value = new ResourceInput("fixture", "", 0, sample);
    Reject(() => value.Decode("guess")); Reject(() => value.Decode("yj1"));
});
Check("RGBA PNG preserves opaque black and white plus transparent color", () =>
{
    PngFile.Write(Path.Combine(output, "rgba.png"), 4, 1, new byte[] { 255,255,255,0, 0,0,0,255, 255,255,255,255, 23,48,92,128 });
    Equal("89504E470D0A1A0A", Convert.ToHexString(File.ReadAllBytes(Path.Combine(output, "rgba.png")).AsSpan(0,8)));
});
foreach (var (format, bytes) in new[] { ("yj1", YjFixtures.Dos), ("yj2", YjFixtures.Win) })
{
    Check(format + " matches independently encoded synthetic bytes", () =>
        Equal(ResourceInput.Hash(YjFixtures.Raw), ResourceInput.Hash(new ResourceInput("fixture", "", 0, bytes).Decode(format))));
    Check(format + " rejects every truncated prefix", () =>
    {
        for (int length = 0; length < bytes.Length; length++)
            Reject(() => new ResourceInput("fixture", "", 0, bytes[..length]).Decode(format));
    });
    foreach (int declared in new[] { 1, YjFixtures.Raw.Length - 1, YjFixtures.Raw.Length + 1 })
        Check(format + " rejects inconsistent output length " + declared, () =>
        {
            var changed = bytes.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(changed.AsSpan(format == "yj1" ? 4 : 0), declared);
            Reject(() => new ResourceInput("fixture", "", 0, changed).Decode(format));
        });
}
byte[] UncompressedDos(int declared, params byte[] payload)
{
    var data = new byte[20 + payload.Length]; "YJ_1"u8.CopyTo(data);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), declared);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), data.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(16), (ushort)payload.Length);
    payload.CopyTo(data, 20); return data;
}
Check("YJ1 uncompressed block needs no Huffman tree", () =>
    Equal("23,255", string.Join(",", new ResourceInput("fixture", "", 0, UncompressedDos(2, 23, 255)).Decode("yj1"))));
Check("YJ1 uncompressed block cannot exceed the declared output", () =>
    Reject(() => new ResourceInput("fixture", "", 0, UncompressedDos(1, 23, 255)).Decode("yj1")));
Check("YJ1 rejects an out-of-range Huffman child", () =>
{
    var data = YjFixtures.Dos; data[16] = 255;
    Reject(() => new ResourceInput("fixture", "", 0, data).Decode("yj1"));
});
Check("YJ1 rejects a cyclic Huffman table", () =>
{
    var data = YjFixtures.Dos;
    // Both first-level internal nodes point back to nodes 1 and 2.
    data[16] = data[17] = 0;
    Reject(() => new ResourceInput("fixture", "", 0, data).Decode("yj1"));
});
void RejectPath(Action action)
{
    try { action(); } catch (IOException) { return; }
    throw new Exception("Unsafe or existing output was accepted.");
}
Check("Output cannot overwrite an existing directory", () => RejectPath(() => ExportPaths.Validate(output, source)));
Check("Output cannot be inside the source directory", () => RejectPath(() => ExportPaths.Validate(Path.Combine(output, "new-output"), source)));
Check("Output cannot use parent traversal back to the source", () => RejectPath(() => ExportPaths.Validate(Path.Combine(output, "test", "..", "new-output"), source)));
if (OperatingSystem.IsWindows())
{
    foreach (var name in new[] { "parent.", "parent ", "PARENT~1", "file:stream" })
        Check("Alternate Windows path is rejected: " + name, () => RejectPath(() => ExportPaths.Validate(Path.Combine(output, name, "new-output"), source)));
}
byte[] Mkf(byte[] chunk)
{
    var bytes = new byte[chunk.Length + 8]; BinaryPrimitives.WriteInt32LittleEndian(bytes, 8);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length); chunk.CopyTo(bytes, 8); return bytes;
}
var fixtureRoot = Path.Combine(output, "cli-input"); Directory.CreateDirectory(fixtureRoot);
var cliSource = Path.Combine(fixtureRoot, "DATA.MKF"); var cliPalette = Path.Combine(fixtureRoot, "PAT.MKF");
File.WriteAllBytes(cliSource, Mkf(ZeroTail(3))); File.WriteAllBytes(cliPalette, Mkf(testPalette));
int Export(string name, params string[] extra)
{
    string[] arguments = ["--mkf", cliSource, "--chunk", "0", "--kind", "sprite", "--compression", "none", "--family", "DATA", "--palette", cliPalette, "--palette-chunk", "0", "--variant", "day", "--output", Path.Combine(output, name)];
    var stdout = Console.Out; var stderr = Console.Error; using var capture = new StringWriter();
    try { Console.SetOut(capture); Console.SetError(capture); return PalAssetExport.Program.Main(arguments.Concat(extra).ToArray()); }
    finally { Console.SetOut(stdout); Console.SetError(stderr); File.WriteAllText(Path.Combine(output, name + ".log"), capture.ToString()); }
}
Check("Old CLI flags retain identical PNG bytes and export all zero-tail frames", () =>
{
    Equal(0, Export("default")); Equal(3, Directory.GetFiles(Path.Combine(output, "default"), "*.png").Length);
    var expected = new byte[16]; var image = RlePixels.Read(sample);
    for (var i = 0; i < 4; i++)
    {
        if (i > 0) for (var c = 0; c < 3; c++) expected[i * 4 + c] = (byte)(testPalette[image.Indices[i] * 3 + c] * 4);
        expected[i * 4 + 3] = image.Alpha[i];
    }
    var expectedFile = Path.Combine(output, "expected.png"); PngFile.Write(expectedFile, 4, 1, expected);
    Equal(ResourceInput.FileHash(expectedFile), ResourceInput.FileHash(Path.Combine(output, "default", "00002.png")));
});
Check("CLI selected frames retain source IDs in filenames and provenance", () =>
{
    var sourceHash = ResourceInput.FileHash(cliSource); var paletteHash = ResourceInput.FileHash(cliPalette);
    Equal(0, Export("selected", "--frames", "2,1", "--color-bank", "1", "--shade-shift", "-4"));
    Equal("00001.png,00002.png", string.Join(",", Directory.GetFiles(Path.Combine(output, "selected"), "*.png").Select(Path.GetFileName).Order()));
    using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "selected", "export.json")));
    Equal(1, receipt.RootElement.GetProperty("frames")[0].GetProperty("frame").GetInt32());
    Equal(3, receipt.RootElement.GetProperty("selection").GetProperty("source_frame_count").GetInt32());
    Equal(1, receipt.RootElement.GetProperty("indexed_transform").GetProperty("color_bank").GetInt32());
    Equal(-4, receipt.RootElement.GetProperty("indexed_transform").GetProperty("shade_shift").GetInt32());
    Equal(sourceHash, ResourceInput.FileHash(cliSource)); Equal(paletteHash, ResourceInput.FileHash(cliPalette));
});
Check("Invalid optional CLI parameters create no output", () =>
{
    Equal(1, Export("bad-options", "--shade-shift", "0")); Equal(false, Directory.Exists(Path.Combine(output, "bad-options")));
});
Check("Selecting a valid frame still validates the whole offset table", () =>
{
    var data = ZeroTail(3); BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 0); File.WriteAllBytes(cliSource, Mkf(data));
    Equal(1, Export("bad-table-selection", "--frames", "0")); Equal(false, Directory.Exists(Path.Combine(output, "bad-table-selection")));
});
Check("Selected frame export does not claim to validate unselected RLE", () =>
{
    File.WriteAllBytes(cliSource, Mkf(ZeroTail(3)[..^2]));
    Equal(0, Export("selected-valid-rle", "--frames", "0"));
    Equal("00000.png", string.Join(",", Directory.GetFiles(Path.Combine(output, "selected-valid-rle"), "*.png").Select(Path.GetFileName)));
    using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "selected-valid-rle", "export.json")));
    Equal(3, receipt.RootElement.GetProperty("selection").GetProperty("source_frame_count").GetInt32());
    Equal("[0]", JsonSerializer.Serialize(receipt.RootElement.GetProperty("selection").GetProperty("source_frames")));
    Equal(1, Export("all-truncated-rle")); Equal(false, Directory.Exists(Path.Combine(output, "all-truncated-rle")));
});
var result = new { success = failures == 0, failed = failures, checks };
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(result));
return failures == 0 ? 0 : 1;
