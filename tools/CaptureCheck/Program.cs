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
    Console.Error.WriteLine("CaptureCheck --hotkeys [seconds]");
    Console.Error.WriteLine("CaptureCheck --flow region|window|full|monitor|repeat <outputFolder> [monitorIndex] [--busy]");
    Console.Error.WriteLine("  Prints the monitor table, captures the virtual screen and writes composite.png plus monitor-N.png.");
    Console.Error.WriteLine("  --overlay shows the selection overlay on the capture and writes selection.png for the confirmed region.");
    Console.Error.WriteLine("  --hotkeys registers the default hotkeys, prints each binding's status and reports presses for the given seconds (default 10).");
    Console.Error.WriteLine("  --flow runs the CaptureCoordinator end to end and writes flow.png; --busy fires a second request while the first runs.");
    return 2;
}

if (args[0] == "--hotkeys")
{
    // --hotkeys [seconds] [region window fullscreen repeat]: optional gesture overrides for testing on a machine where the defaults are taken.
    var seconds = args.Length > 1 && int.TryParse(args[1], CultureInfo.InvariantCulture, out var s) ? s : 10;
    var defaults = new ScrubShot.Core.Settings.HotkeySettings();
    var settings = args.Length >= 6
        ? defaults with { Region = args[2], Window = args[3], FullScreen = args[4], RepeatLastRegion = args[5] }
        : defaults;
    Console.WriteLine($"Snipping Tool owns Print Screen (Windows switch): {ScrubShot.Core.Hotkeys.SnippingToolPrintScreen.IsEnabled()}");
    using var hotkeys = ScrubShot.Capture.Hotkeys.HotkeyService.Start();
    hotkeys.Pressed += action => Console.WriteLine($"Pressed: {action} at {DateTime.Now:HH:mm:ss.fff}");
    foreach (var binding in hotkeys.Apply(settings))
    {
        Console.WriteLine($"{binding.Action,-17} {binding.GestureText,-20} {(binding.IsRegistered ? "registered" : binding.Conflict.ToString())}{(binding.ErrorCode != 0 ? $" (error {binding.ErrorCode})" : string.Empty)}");
    }

    Console.WriteLine($"Listening for {seconds} s...");
    await Task.Delay(TimeSpan.FromSeconds(seconds));
    Console.WriteLine("Done.");
    return 0;
}

if (args[0] == "--flow")
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("--flow needs a mode and an output folder.");
        return 2;
    }

    var folder = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(folder);
    var monitorIndex = args.Length > 3 && int.TryParse(args[3], CultureInfo.InvariantCulture, out var mi) ? mi : 0;
    var request = args[1].ToLowerInvariant() switch
    {
        "region" => ScrubShot.Core.Coordination.CaptureRequest.Region,
        "window" => ScrubShot.Core.Coordination.CaptureRequest.Window,
        "full" => ScrubShot.Core.Coordination.CaptureRequest.FullScreen,
        "monitor" => ScrubShot.Core.Coordination.CaptureRequest.ForMonitor(monitorIndex),
        "repeat" => ScrubShot.Core.Coordination.CaptureRequest.RepeatLastRegion,
        _ => throw new ArgumentException($"Unknown flow mode '{args[1]}'."),
    };

    var sink = new PngFolderSink(folder);
    var coordinator = new ScrubShot.Capture.Coordination.CaptureCoordinator(
        new GdiScreenCapturer(),
        MonitorEnumerator.GetLayout,
        ScrubShot.Capture.Coordination.OverlayRegionSelector.Instance,
        sink,
        () => new ScrubShot.Core.Settings.CaptureSettings());

    var flowWatch = Stopwatch.StartNew();
    var first = coordinator.CaptureAsync(request);
    if (args.Contains("--busy"))
    {
        var second = await coordinator.CaptureAsync(ScrubShot.Core.Coordination.CaptureRequest.FullScreen);
        Console.WriteLine($"Second request while busy: {second}");
    }

    var outcome = await first;
    flowWatch.Stop();
    Console.WriteLine($"Flow {request.Mode}: {outcome} in {flowWatch.ElapsedMilliseconds} ms; last region: {coordinator.LastRegion?.ToString() ?? "none"}");
    if (sink.Last is { } last)
    {
        Console.WriteLine($"flow.png: {last.Width}x{last.Height} from {last.ScreenRect}");
    }

    return outcome == ScrubShot.Core.Coordination.CaptureOutcome.Completed ? 0 : 3;
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

/// <summary>Development-only sink: writes the capture as flow.png in the chosen folder, by explicit request (D-037).</summary>
file sealed class PngFolderSink(string folder) : ScrubShot.Capture.Coordination.ICaptureSink
{
    public CapturedImage? Last { get; private set; }

    public async Task HandleAsync(CapturedImage image, ScrubShot.Core.Coordination.CaptureRequest request, CancellationToken cancellationToken)
    {
        Last = image;
        using var file = File.Create(Path.Combine(folder, "flow.png"));
        using var stream = file.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)image.Width, (uint)image.Height, 96, 96, image.Pixels);
        await encoder.FlushAsync();
    }
}
