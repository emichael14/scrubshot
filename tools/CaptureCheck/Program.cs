// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Diagnostics;
using System.Globalization;
using ScrubShot.Capture;
using ScrubShot.Capture.Overlay;
using ScrubShot.Core.Geometry;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.Error.WriteLine("CaptureCheck <outputFolder> [--cursor] [--overlay region|window] [--no-confirm-on-release]");
    Console.Error.WriteLine("  Prints the monitor table, captures the virtual screen and writes composite.png plus monitor-N.png.");
    Console.Error.WriteLine("  --overlay shows the selection overlay on the capture and writes selection.png for the confirmed region.");
    return 2;
}

var outputFolder = Path.GetFullPath(args[0]);
var includeCursor = args.Contains("--cursor");
var overlayIndex = Array.IndexOf(args, "--overlay");
OverlayMode? overlayMode = overlayIndex >= 0 && overlayIndex + 1 < args.Length
    ? args[overlayIndex + 1].Equals("window", StringComparison.OrdinalIgnoreCase) ? OverlayMode.Window : OverlayMode.Region
    : null;
var confirmOnRelease = !args.Contains("--no-confirm-on-release");
Directory.CreateDirectory(outputFolder);

Console.WriteLine($"PerMonitorV2: {ProcessDpi.IsPerMonitorV2()}");
var layout = MonitorEnumerator.GetLayout();
Console.WriteLine($"Virtual screen: {layout.VirtualScreen} (mixed DPI: {layout.HasMixedDpi})");
Console.WriteLine("Index  Primary  DPI  Scale  Bounds (virtual screen)        Work area                     Device");
foreach (var m in layout.Monitors)
{
    Console.WriteLine($"{m.Index,-6} {(m.IsPrimary ? "yes" : "no"),-8} {m.Dpi,-4} {m.Scale.ToString("0.00", CultureInfo.InvariantCulture),-6} {m.Bounds,-30} {m.WorkArea,-30} {m.DeviceName}");
}

var stopwatch = Stopwatch.StartNew();
using var frozen = new GdiScreenCapturer().Capture(layout, new CaptureOptions { IncludeCursor = includeCursor });
stopwatch.Stop();
Console.WriteLine($"Captured {frozen.Width}x{frozen.Height} in {stopwatch.ElapsedMilliseconds} ms (cursor: {frozen.IncludesCursor})");

if (overlayMode is { } mode)
{
    var options = new OverlayOptions { ConfirmOnRelease = confirmOnRelease };
    stopwatch.Restart();
    var result = await OverlaySession.RunOnNewThreadAsync(frozen, mode, options);
    stopwatch.Stop();
    Console.WriteLine($"Overlay ({mode}): {result.Outcome} after {stopwatch.ElapsedMilliseconds} ms");
    if (result.Outcome == OverlayOutcome.Confirmed)
    {
        Console.WriteLine($"Region: {result.Region} window: 0x{result.WindowHandle:X}");
        var selection = frozen.Crop(layout.ToBitmap(result.Region));
        await WritePngAsync(Path.Combine(outputFolder, "selection.png"), selection);
        Console.WriteLine($"selection.png: {selection.Width}x{selection.Height}");
    }

    Console.WriteLine($"Written to {outputFolder}");
    return result.Outcome == OverlayOutcome.Confirmed ? 0 : 3;
}

var composite = frozen.Crop(frozen.Bounds);
await WritePngAsync(Path.Combine(outputFolder, "composite.png"), composite);
foreach (var m in layout.Monitors)
{
    var image = frozen.Crop(layout.ToBitmap(m.Bounds));
    await WritePngAsync(Path.Combine(outputFolder, $"monitor-{m.Index}.png"), image);
    Console.WriteLine($"monitor-{m.Index}.png: {image.Width}x{image.Height} from {image.ScreenRect}");
}

Console.WriteLine($"Written to {outputFolder}");
return 0;

static async Task WritePngAsync(string path, CapturedImage image)
{
    using var file = File.Create(path);
    using var stream = file.AsRandomAccessStream();
    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)image.Width, (uint)image.Height, 96, 96, image.Pixels);
    await encoder.FlushAsync();
}
