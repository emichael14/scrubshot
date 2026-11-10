// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Globalization;
using SkiaSharp;
using Svg.Skia;

// Sizes written as PNG. The small-size variant is used at or below SmallVariantMaxSize.
int[] pngSizes = [16, 20, 24, 32, 48, 64, 128, 256, 512];
int[] icoSizes = [16, 20, 24, 32, 48, 64, 256];
const int SmallVariantMaxSize = 24;

return args switch
{
    ["render", var svgPath, var pngPath, var size] => Render(svgPath, pngPath, int.Parse(size, CultureInfo.InvariantCulture)),
    ["build", var fullSvg, var smallSvg, var outDir, var name] => Build(fullSvg, smallSvg, outDir, name),
    _ => Usage(),
};

int Usage()
{
    Console.Error.WriteLine("IconBuild render <icon.svg> <out.png> <size>");
    Console.Error.WriteLine("IconBuild build <full.svg> <small.svg|-> <outDir> <name>");
    Console.Error.WriteLine("  Writes <name>-<size>.png for " + string.Join(", ", pngSizes) + ", <name>.ico with "
        + string.Join(", ", icoSizes) + ", and <name>-preview.png (light and dark rows).");
    Console.Error.WriteLine("  Pass - as <small.svg> to use the full icon at every size.");
    return 2;
}

int Render(string svgPath, string pngPath, int size)
{
    using var svg = LoadSvg(svgPath);
    using var bitmap = RenderToBitmap(svg, size);
    SavePng(bitmap, pngPath);
    Console.WriteLine($"{pngPath} ({size}x{size})");
    return 0;
}

int Build(string fullSvgPath, string smallSvgPath, string outDir, string name)
{
    Directory.CreateDirectory(outDir);
    using var full = LoadSvg(fullSvgPath);
    using var small = smallSvgPath == "-" ? null : LoadSvg(smallSvgPath);

    var rendered = new Dictionary<int, SKBitmap>();
    try
    {
        foreach (var size in pngSizes.Union(icoSizes).Distinct().Order())
        {
            var source = small is not null && size <= SmallVariantMaxSize ? small : full;
            rendered[size] = RenderToBitmap(source, size);
        }

        foreach (var size in pngSizes)
        {
            SavePng(rendered[size], Path.Combine(outDir, $"{name}-{size}.png"));
        }

        var icoPath = Path.Combine(outDir, $"{name}.ico");
        File.WriteAllBytes(icoPath, BuildIco(icoSizes.Select(s => rendered[s]).ToList()));

        var previewPath = Path.Combine(outDir, $"{name}-preview.png");
        using (var sheet = BuildPreviewSheet(rendered, [16, 20, 24, 32, 48, 64, 128, 256]))
        {
            SavePng(sheet, previewPath);
        }

        Console.WriteLine($"{outDir}: {pngSizes.Length} PNGs, {Path.GetFileName(icoPath)} ({icoSizes.Length} sizes), {Path.GetFileName(previewPath)}");
        return 0;
    }
    finally
    {
        foreach (var bitmap in rendered.Values)
        {
            bitmap.Dispose();
        }
    }
}

static SKSvg LoadSvg(string path)
{
    var svg = new SKSvg();
    if (svg.Load(path) is null || svg.Picture is null)
    {
        throw new InvalidOperationException($"Could not load SVG: {path}");
    }

    return svg;
}

/// <summary>Renders the SVG into a square, straight-alpha BGRA bitmap of the given size.</summary>
static SKBitmap RenderToBitmap(SKSvg svg, int size)
{
    var picture = svg.Picture ?? throw new InvalidOperationException("SVG has no picture.");
    var bounds = picture.CullRect;
    var scale = size / Math.Max(bounds.Width, bounds.Height);

    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);
    canvas.Scale(scale);
    canvas.Translate(-bounds.Left, -bounds.Top);
    canvas.DrawPicture(picture);
    canvas.Flush();

    using var image = surface.Snapshot();
    var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul));
    if (!image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
    {
        throw new InvalidOperationException("Pixel readback failed.");
    }

    return bitmap;
}

static void SavePng(SKBitmap bitmap, string path)
{
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(path, data.ToArray());
}

static byte[] EncodePng(SKBitmap bitmap)
{
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

/// <summary>
/// Builds a Windows .ico: 32-bit BGRA DIB entries with an AND mask for sizes up to 64, PNG entry for 256.
/// </summary>
static byte[] BuildIco(IReadOnlyList<SKBitmap> bitmaps)
{
    var images = bitmaps.Select(b => (Bitmap: b, Data: b.Width >= 256 ? EncodePng(b) : EncodeDib(b))).ToList();

    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);

    writer.Write((ushort)0);                 // reserved
    writer.Write((ushort)1);                 // type: icon
    writer.Write((ushort)images.Count);

    var offset = 6 + (16 * images.Count);
    foreach (var (bitmap, data) in images)
    {
        writer.Write((byte)(bitmap.Width >= 256 ? 0 : bitmap.Width));
        writer.Write((byte)(bitmap.Height >= 256 ? 0 : bitmap.Height));
        writer.Write((byte)0);               // color count
        writer.Write((byte)0);               // reserved
        writer.Write((ushort)1);             // planes
        writer.Write((ushort)32);            // bits per pixel
        writer.Write(data.Length);
        writer.Write(offset);
        offset += data.Length;
    }

    foreach (var (_, data) in images)
    {
        writer.Write(data);
    }

    writer.Flush();
    return stream.ToArray();
}

/// <summary>Encodes a straight-alpha bitmap as a bottom-up 32-bit DIB followed by a 1-bit AND mask.</summary>
static byte[] EncodeDib(SKBitmap bitmap)
{
    var width = bitmap.Width;
    var height = bitmap.Height;
    var maskStride = (width + 31) / 32 * 4;
    var pixels = bitmap.Pixels;

    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);

    writer.Write(40);                        // BITMAPINFOHEADER size
    writer.Write(width);
    writer.Write(height * 2);                // XOR + AND
    writer.Write((ushort)1);                 // planes
    writer.Write((ushort)32);                // bit count
    writer.Write(0);                         // BI_RGB
    writer.Write((width * height * 4) + (maskStride * height));
    writer.Write(0);                         // x pixels per meter
    writer.Write(0);                         // y pixels per meter
    writer.Write(0);                         // colors used
    writer.Write(0);                         // colors important

    for (var y = height - 1; y >= 0; y--)
    {
        for (var x = 0; x < width; x++)
        {
            var color = pixels[(y * width) + x];
            writer.Write(color.Blue);
            writer.Write(color.Green);
            writer.Write(color.Red);
            writer.Write(color.Alpha);
        }
    }

    var maskRow = new byte[maskStride];
    for (var y = height - 1; y >= 0; y--)
    {
        Array.Clear(maskRow);
        for (var x = 0; x < width; x++)
        {
            if (pixels[(y * width) + x].Alpha == 0)
            {
                maskRow[x / 8] |= (byte)(0x80 >> (x % 8));
            }
        }

        writer.Write(maskRow);
    }

    writer.Flush();
    return stream.ToArray();
}

/// <summary>
/// Two rows (light and dark taskbar colors). Each size is drawn 1:1 and, for 32 px and below, also
/// at 3x with nearest-neighbor sampling so individual pixels can be judged.
/// </summary>
static SKBitmap BuildPreviewSheet(IReadOnlyDictionary<int, SKBitmap> rendered, int[] sizes)
{
    const int Gap = 24;
    const int LabelHeight = 22;
    const int ZoomFactor = 3;
    const int ZoomMaxSize = 32;

    var cellWidths = sizes.Select(s => s <= ZoomMaxSize ? s + 8 + (s * ZoomFactor) : s).ToArray();
    var rowHeight = sizes.Max() + LabelHeight + Gap;
    var width = Gap + cellWidths.Sum() + (Gap * sizes.Length);
    var height = (rowHeight * 2) + Gap;

    var sheet = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
    using var canvas = new SKCanvas(sheet);
    using var font = new SKFont(SKTypeface.Default, 13);
    using var lightText = new SKPaint { Color = new SKColor(0x1F, 0x1F, 0x1F), IsAntialias = true };
    using var darkText = new SKPaint { Color = new SKColor(0xE6, 0xE6, 0xE6), IsAntialias = true };
    var nearest = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

    var rows = new[]
    {
        (Background: new SKColor(0xF3, 0xF3, 0xF3), Text: lightText, Label: "light"),
        (Background: new SKColor(0x20, 0x20, 0x20), Text: darkText, Label: "dark"),
    };

    for (var row = 0; row < rows.Length; row++)
    {
        var top = row * rowHeight;
        canvas.DrawRect(0, top, width, rowHeight + (row == rows.Length - 1 ? Gap : 0), new SKPaint { Color = rows[row].Background });

        var x = Gap;
        var baseline = top + Gap + sizes.Max();
        for (var i = 0; i < sizes.Length; i++)
        {
            var size = sizes[i];
            using var image = SKImage.FromBitmap(rendered[size]);
            canvas.DrawImage(image, x, baseline - size, nearest);
            if (size <= ZoomMaxSize)
            {
                var zoom = size * ZoomFactor;
                var dest = SKRect.Create(x + size + 8, baseline - zoom, zoom, zoom);
                canvas.DrawImage(image, dest, nearest);
            }

            canvas.DrawText($"{size} px", x, baseline + 16, SKTextAlign.Left, font, rows[row].Text);
            x += cellWidths[i] + Gap;
        }
    }

    canvas.Flush();
    return sheet;
}
