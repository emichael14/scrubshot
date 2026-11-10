// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using ScrubShot.Capture;
using ScrubShot.Core.Coordination;
using ScrubShot.Core.Geometry;
using ScrubShot.Core.Output;
using ScrubShot.Core.Settings;
using ScrubShot.Output;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ScrubShot.Tests.Output;

internal static class TestImages
{
    public static readonly DateTimeOffset Stamp = new(2026, 11, 1, 13, 5, 9, TimeSpan.Zero);

    /// <summary>A w x h image where B = x, G = y, R = 7, A = 255, at virtual-screen (10, 20).</summary>
    public static CapturedImage Gradient(int width, int height)
    {
        var layout = new MonitorLayout([new MonitorInfo(0, "A", new PixelRect(0, 0, Math.Max(width, 10) + 20, Math.Max(height, 10) + 30), new PixelRect(0, 0, 100, 100), true, 96)]);
        using var frozen = FrozenScreen.Allocate(layout, Stamp, includesCursor: false);
        var pixels = frozen.WritablePixels;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = ((20 + y) * frozen.Stride) + ((10 + x) * FrozenScreen.BytesPerPixel);
                pixels[i] = (byte)x;
                pixels[i + 1] = (byte)y;
                pixels[i + 2] = 7;
                pixels[i + 3] = 255;
            }
        }

        return frozen.Crop(new PixelRect(10, 20, width, height));
    }
}

public class DibBuilderTests
{
    [Fact]
    public void Header_describes_a_bottom_up_32bpp_bitmap()
    {
        var image = TestImages.Gradient(3, 2);

        var dib = DibBuilder.Build(image);

        Assert.Equal(40 + (3 * 2 * 4), dib.Length);
        Assert.Equal(40u, BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(0)));
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(4)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(12)));
        Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(16)));
        Assert.Equal(24u, BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(20)));
    }

    [Fact]
    public void Rows_are_flipped_so_the_last_capture_row_comes_first()
    {
        var image = TestImages.Gradient(3, 2);

        var dib = DibBuilder.Build(image);

        // First stored row is capture row 1 (G = 1), second is capture row 0 (G = 0); B is the column either way.
        Assert.Equal(1, dib[40 + 1]);
        Assert.Equal(0, dib[40]);
        Assert.Equal(2, dib[40 + (2 * 4)]);
        Assert.Equal(0, dib[40 + 12 + 1]);
        Assert.Equal(7, dib[40 + 12 + 2]);
    }
}

public class ImageEncoderTests
{
    [Fact]
    public async Task Png_round_trips_the_pixels()
    {
        var image = TestImages.Gradient(5, 4);

        var png = await ImageEncoder.EncodePngAsync(image, TestContext.Current.CancellationToken);

        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png.Take(4).ToArray());
        var (width, height, pixels) = await Decode(png);
        Assert.Equal(5u, width);
        Assert.Equal(4u, height);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                var i = ((y * 5) + x) * 4;
                Assert.Equal(x, pixels[i]);
                Assert.Equal(y, pixels[i + 1]);
                Assert.Equal(7, pixels[i + 2]);
                Assert.Equal(255, pixels[i + 3]);
            }
        }
    }

    [Fact]
    public async Task Jpg_has_the_right_size_and_quality_changes_the_byte_count()
    {
        var image = TestImages.Gradient(64, 48);

        var high = await ImageEncoder.EncodeJpgAsync(image, 95, TestContext.Current.CancellationToken);
        var low = await ImageEncoder.EncodeJpgAsync(image, 10, TestContext.Current.CancellationToken);

        Assert.Equal([0xFF, 0xD8], high.Take(2).ToArray());
        var (width, height, _) = await Decode(high);
        Assert.Equal(64u, width);
        Assert.Equal(48u, height);
        Assert.True(low.Length < high.Length, $"low {low.Length} should be smaller than high {high.Length}");
    }

    [Fact]
    public async Task Format_dispatch_and_extension_agree()
    {
        var image = TestImages.Gradient(2, 2);

        var png = await ImageEncoder.EncodeAsync(image, ImageFormat.Png, 90, TestContext.Current.CancellationToken);
        var jpg = await ImageEncoder.EncodeAsync(image, ImageFormat.Jpg, 90, TestContext.Current.CancellationToken);

        Assert.Equal(0x89, png[0]);
        Assert.Equal(0xFF, jpg[0]);
        Assert.Equal(".png", ImageEncoder.ExtensionFor(ImageFormat.Png));
        Assert.Equal(".jpg", ImageEncoder.ExtensionFor(ImageFormat.Jpg));
    }

    private static async Task<(uint Width, uint Height, byte[] Pixels)> Decode(byte[] encoded)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(encoded.AsBuffer());
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        return (decoder.PixelWidth, decoder.PixelHeight, data.DetachPixelData());
    }
}

/// <summary>The clipboard write sequence against a scripted target: order of formats, and what is left behind when a step fails.</summary>
public class ClipboardWriteSequenceTests
{
    private static readonly ClipboardFormatIds Ids = new(Png: 1, Dib: 2, ExcludeFromMonitor: 3, CanIncludeInHistory: 4, CanUploadToCloud: 5);
    private static readonly byte[] Png = [9, 9, 9];
    private static readonly byte[] Dib = [8, 8];

    [Fact]
    public void Exclusion_markers_go_on_before_any_pixels()
    {
        var target = new ScriptedTarget();

        ClipboardWriter.Write(target, Ids, Png, Dib, excludeFromHistory: true);

        Assert.Equal(["empty", "put 3", "put 4", "put 5", "put 1", "put 2"], target.Calls);
        Assert.Equal([0, 0, 0, 0], target.Data[4]);
        Assert.Equal([0, 0, 0, 0], target.Data[5]);
        Assert.Equal(Png, target.Data[1]);
        Assert.Equal(Dib, target.Data[2]);
    }

    [Fact]
    public void Without_exclusion_only_the_image_formats_are_written_png_first()
    {
        var target = new ScriptedTarget();

        ClipboardWriter.Write(target, Ids, Png, Dib, excludeFromHistory: false);

        Assert.Equal(["empty", "put 1", "put 2"], target.Calls);
    }

    [Theory]
    [InlineData(1)] // first marker
    [InlineData(3)] // last marker
    [InlineData(4)] // PNG
    [InlineData(5)] // DIB, after the PNG is already on the clipboard
    public void A_failure_at_any_step_empties_the_clipboard_and_rethrows(int failAtPut)
    {
        var target = new ScriptedTarget { FailAtPut = failAtPut };

        var thrown = Assert.Throws<Win32Exception>(() => ClipboardWriter.Write(target, Ids, Png, Dib, excludeFromHistory: true));

        Assert.Equal(8, thrown.NativeErrorCode);
        Assert.Equal("empty", target.Calls[^1]);
        Assert.Empty(target.Data); // nothing half-written is left for history or cloud sync to pick up
    }

    [Fact]
    public void A_failed_rollback_still_reports_the_original_failure()
    {
        var target = new ScriptedTarget { FailAtPut = 5, FailSecondEmpty = true };

        var thrown = Assert.Throws<Win32Exception>(() => ClipboardWriter.Write(target, Ids, Png, Dib, excludeFromHistory: true));

        Assert.Equal(8, thrown.NativeErrorCode);
    }

    [Fact]
    public void When_the_first_empty_fails_nothing_is_written()
    {
        var target = new ScriptedTarget { FailFirstEmpty = true };

        Assert.Throws<Win32Exception>(() => ClipboardWriter.Write(target, Ids, Png, Dib, excludeFromHistory: true));

        Assert.Equal(["empty"], target.Calls);
    }

    private sealed class ScriptedTarget : IClipboardTarget
    {
        private int _puts;
        private int _empties;

        public List<string> Calls { get; } = [];

        public Dictionary<uint, byte[]> Data { get; } = [];

        public int FailAtPut { get; init; }

        public bool FailFirstEmpty { get; init; }

        public bool FailSecondEmpty { get; init; }

        public void Empty()
        {
            Calls.Add("empty");
            _empties++;
            if ((_empties == 1 && FailFirstEmpty) || (_empties == 2 && FailSecondEmpty))
            {
                throw new Win32Exception(5);
            }

            Data.Clear();
        }

        public void Put(uint format, ReadOnlySpan<byte> data)
        {
            if (++_puts == FailAtPut)
            {
                throw new Win32Exception(8);
            }

            Calls.Add($"put {format}");
            Data[format] = data.ToArray();
        }
    }
}

public class CaptureSaverTests : IDisposable
{
    private static readonly string Parent = Path.Combine(Path.GetTempPath(), "ScrubShotTests");
    private readonly string _root = Path.Combine(Parent, Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }

            // The shared parent is left in place: other test classes create their own folders under it in
            // parallel, and removing it "when empty" races with them.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leftovers are harmless fake bytes.
        }
    }

    [Theory]
    [InlineData(@"C:\Shots", @"C:\Shots")]
    [InlineData(@"Shots\Sub", @"C:\Users\x\Pictures\Shots\Sub")]
    [InlineData("", @"C:\Users\x\Pictures\ScrubShot")]
    [InlineData("   ", @"C:\Users\x\Pictures\ScrubShot")]
    [InlineData(@"\\server\share\shots", @"\\server\share\shots")]
    public void Folder_resolves_rooted_relative_and_empty_values(string configured, string expected)
    {
        Assert.Equal(expected, SaveFolder.Resolve(configured, @"C:\Users\x\Pictures"));
    }

    [Fact]
    public void Folder_expands_environment_variables()
    {
        Environment.SetEnvironmentVariable("SCRUBSHOT_TEST_DIR", @"D:\Caps");
        try
        {
            Assert.Equal(@"D:\Caps\x", SaveFolder.Resolve(@"%SCRUBSHOT_TEST_DIR%\x", @"C:\Users\x\Pictures"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SCRUBSHOT_TEST_DIR", null);
        }
    }

    [Theory]
    [InlineData(@"\Shots", @"C:\Users\x\Pictures", SaveFolderProblem.NotFullyQualified)]   // would resolve against the current drive
    [InlineData(@"D:Shots", @"C:\Users\x\Pictures", SaveFolderProblem.NotFullyQualified)]  // would resolve against D:'s current directory
    [InlineData("", "", SaveFolderProblem.PicturesUnavailable)]
    [InlineData(@"Shots\Sub", "", SaveFolderProblem.PicturesUnavailable)]
    [InlineData("Shots", null, SaveFolderProblem.PicturesUnavailable)]
    [InlineData("Shots", @"Pictures", SaveFolderProblem.PicturesUnavailable)]              // a relative Pictures path is no anchor
    [InlineData(@"%SCRUBSHOT_NOT_DEFINED%\x", @"C:\Users\x\Pictures", SaveFolderProblem.UnresolvedVariable)]
    public void Folder_values_that_would_depend_on_the_current_directory_are_refused(string configured, string? pictures, SaveFolderProblem expected)
    {
        Assert.False(SaveFolder.TryResolve(configured, pictures, out var folder, out var problem));
        Assert.Equal(expected, problem);
        Assert.Equal(string.Empty, folder);

        var thrown = Assert.Throws<IOException>(() => SaveFolder.Resolve(configured, pictures));
        if (configured.Length > 0)
        {
            Assert.DoesNotContain(configured, thrown.Message, StringComparison.OrdinalIgnoreCase); // nothing user-specific may reach a log
        }
    }

    [Fact]
    public void An_absolute_folder_needs_no_pictures_folder()
    {
        Assert.True(SaveFolder.TryResolve(@"C:\X", string.Empty, out var folder, out var problem));
        Assert.Equal(@"C:\X", folder);
        Assert.Equal(SaveFolderProblem.None, problem);
    }

    [Fact]
    public void Resolved_folders_never_depend_on_the_current_directory()
    {
        foreach (var configured in new[] { string.Empty, "Shots", @"C:\Abs", @"..\Up" })
        {
            Assert.True(SaveFolder.TryResolve(configured, @"C:\Users\x\Pictures", out var folder, out _));
            Assert.True(Path.IsPathFullyQualified(folder), folder);
            Assert.DoesNotContain(Environment.CurrentDirectory, folder, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void File_name_follows_the_pattern_and_format()
    {
        var settings = new OutputSettings { FileNamePattern = "shot_${YYYY}${MM}${DD}", Format = ImageFormat.Jpg };

        Assert.Equal("shot_20261101.jpg", CaptureSaver.FileNameFor(settings, TestImages.Stamp));
        Assert.Equal("2026-11-01_13-05-09.png", CaptureSaver.FileNameFor(new OutputSettings(), TestImages.Stamp));
    }

    [Fact]
    public async Task Save_creates_the_folder_writes_the_bytes_and_never_overwrites()
    {
        var settings = new OutputSettings { SaveFolder = Path.Combine(_root, "deep", "er"), FileNamePattern = "fixed" };
        byte[] first = [1, 2, 3];
        byte[] second = [4, 5, 6, 7];

        var path1 = await CaptureSaver.Instance.SaveAsync(first, settings, TestImages.Stamp, TestContext.Current.CancellationToken);
        var path2 = await CaptureSaver.Instance.SaveAsync(second, settings, TestImages.Stamp, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(settings.SaveFolder, "fixed.png"), path1);
        Assert.Equal(Path.Combine(settings.SaveFolder, "fixed-2.png"), path2);
        Assert.Equal(first, await File.ReadAllBytesAsync(path1, TestContext.Current.CancellationToken));
        Assert.Equal(second, await File.ReadAllBytesAsync(path2, TestContext.Current.CancellationToken));
        Assert.Equal(2, Directory.GetFiles(settings.SaveFolder).Length);
    }

    [Fact]
    public async Task Losing_the_race_for_a_name_moves_to_the_next_name_and_leaves_the_other_file_alone()
    {
        var settings = new OutputSettings { SaveFolder = _root, FileNamePattern = "raced" };
        Directory.CreateDirectory(_root);
        var taken = Path.Combine(_root, "raced.png");
        byte[] theirs = [42];
        await File.WriteAllBytesAsync(taken, theirs, TestContext.Current.CancellationToken);

        // The existence check says the name is free although another writer has just created it.
        var saver = new CaptureSaver(exists: _ => false);
        var path = await saver.SaveAsync(new byte[] { 1, 2 }, settings, TestImages.Stamp, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_root, "raced-2.png"), path);
        Assert.Equal(theirs, await File.ReadAllBytesAsync(taken, TestContext.Current.CancellationToken));
        Assert.Equal(2, Directory.GetFiles(_root).Length);
    }

    [Fact]
    public async Task A_cancelled_write_leaves_no_file_behind_and_is_not_retried_under_another_name()
    {
        var settings = new OutputSettings { SaveFolder = _root, FileNamePattern = "partial" };
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // The file is created before the write observes the token, so this exercises the cleanup path.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CaptureSaver.Instance.SaveAsync(new byte[] { 1, 2, 3 }, settings, TestImages.Stamp, cancelled.Token));

        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Save_rejects_empty_payloads()
    {
        var settings = new OutputSettings { SaveFolder = _root };

        await Assert.ThrowsAsync<ArgumentException>(() => CaptureSaver.Instance.SaveAsync(ReadOnlyMemory<byte>.Empty, settings, TestImages.Stamp, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task A_folder_that_cannot_be_resolved_fails_before_anything_is_created()
    {
        var settings = new OutputSettings { SaveFolder = @"\NotAbsolute" };

        await Assert.ThrowsAsync<IOException>(() => CaptureSaver.Instance.SaveAsync(new byte[] { 1 }, settings, TestImages.Stamp, TestContext.Current.CancellationToken));
    }
}

public class OutputSinkTests
{
    [Fact]
    public async Task Copy_action_writes_png_and_dib_with_exclusion_and_saves_nothing()
    {
        var h = new Harness(new OutputSettings { DefaultAction = DefaultCaptureAction.Copy });
        var image = TestImages.Gradient(4, 3);

        await h.Sink.HandleAsync(image, CaptureRequest.Region, TestContext.Current.CancellationToken);

        var write = Assert.Single(h.Clipboard.Writes);
        Assert.True(write.Exclude);
        Assert.Equal(0x89, write.Png[0]);
        Assert.Equal(40 + (4 * 3 * 4), write.Dib.Length);
        Assert.Empty(h.Saver.Saves);
        var result = Assert.Single(h.Completed);
        Assert.True(result.Copied);
        Assert.Null(result.SavedPath);
        Assert.True(result.ExcludedFromHistory);
        Assert.False(result.AnyFailed);
        Assert.True(h.Sink.HasLastCapture);
    }

    [Fact]
    public async Task Exclusion_setting_off_is_passed_through()
    {
        var h = new Harness(new OutputSettings { DefaultAction = DefaultCaptureAction.Copy, ExcludeFromClipboardHistory = false });

        await h.Sink.HandleAsync(TestImages.Gradient(2, 2), CaptureRequest.Region, TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(h.Clipboard.Writes).Exclude);
        Assert.False(Assert.Single(h.Completed).ExcludedFromHistory);
    }

    [Fact]
    public async Task Save_action_encodes_in_the_configured_format_and_touches_no_clipboard()
    {
        var h = new Harness(new OutputSettings { DefaultAction = DefaultCaptureAction.Save, Format = ImageFormat.Jpg, JpgQuality = 80 });

        await h.Sink.HandleAsync(TestImages.Gradient(8, 8), CaptureRequest.FullScreen, TestContext.Current.CancellationToken);

        Assert.Empty(h.Clipboard.Writes);
        var save = Assert.Single(h.Saver.Saves);
        Assert.Equal(0xFF, save.Bytes[0]);
        Assert.Equal(ImageFormat.Jpg, save.Settings.Format);
        Assert.Equal(TestImages.Stamp, save.CapturedAt);
        var result = Assert.Single(h.Completed);
        Assert.False(result.Copied);
        Assert.Equal(h.Saver.PathFor(1), result.SavedPath);
    }

    [Fact]
    public async Task Copy_and_save_does_both()
    {
        var h = new Harness(new OutputSettings { DefaultAction = DefaultCaptureAction.CopyAndSave });

        await h.Sink.HandleAsync(TestImages.Gradient(2, 2), CaptureRequest.Window, TestContext.Current.CancellationToken);

        Assert.Single(h.Clipboard.Writes);
        Assert.Single(h.Saver.Saves);
        var result = Assert.Single(h.Completed);
        Assert.True(result.Copied);
        Assert.NotNull(result.SavedPath);
    }

    [Fact]
    public async Task Encoded_buffers_are_zeroed_once_the_outputs_have_them()
    {
        var h = new Harness(new OutputSettings { DefaultAction = DefaultCaptureAction.CopyAndSave });

        await h.Sink.HandleAsync(TestImages.Gradient(6, 6), CaptureRequest.Region, TestContext.Current.CancellationToken);

        var save = Assert.Single(h.Saver.Saves);
        Assert.Contains(save.Bytes, b => b != 0);                     // the saver saw real bytes
        Assert.All(save.Original.ToArray(), b => Assert.Equal(0, b)); // and the sink's buffer is zero afterwards
    }

    [Fact]
    public async Task Copy_only_failure_clears_the_image_reports_it_and_rethrows()
    {
        var h = new Harness(new OutputSettings { DefaultAction = DefaultCaptureAction.Copy });
        h.Clipboard.Throw = true;
        var image = TestImages.Gradient(2, 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Sink.HandleAsync(image, CaptureRequest.Region, TestContext.Current.CancellationToken));

        Assert.All(image.Pixels, b => Assert.Equal(0, b));
        var result = Assert.Single(h.Completed);
        Assert.True(result.CopyFailed);
        Assert.False(result.AnySucceeded);
        Assert.False(h.Sink.HasLastCapture);
    }

    [Fact]
    public async Task Copy_failure_does_not_stop_the_save()
    {
        var h = new Harness(new OutputSettings { DefaultAction = DefaultCaptureAction.CopyAndSave });
        h.Clipboard.Throw = true;
        var image = TestImages.Gradient(2, 2);

        await h.Sink.HandleAsync(image, CaptureRequest.Region, TestContext.Current.CancellationToken);

        Assert.Single(h.Saver.Saves);
        var result = Assert.Single(h.Completed);
        Assert.False(result.Copied);
        Assert.True(result.CopyFailed);
        Assert.False(result.SaveFailed);
        Assert.NotNull(result.SavedPath);
        Assert.Contains(image.Pixels, b => b != 0);
        Assert.Equal(result, h.Sink.LastResult);
    }

    [Fact]
    public async Task Save_failure_after_a_copy_keeps_the_capture_as_the_last_one_and_says_so()
    {
        var h = new Harness(new OutputSettings { DefaultAction = DefaultCaptureAction.CopyAndSave });
        var first = TestImages.Gradient(2, 2);
        await h.Sink.HandleAsync(first, CaptureRequest.Region, TestContext.Current.CancellationToken);
        h.Saver.Throw = true;
        var second = TestImages.Gradient(3, 3);

        await h.Sink.HandleAsync(second, CaptureRequest.Region, TestContext.Current.CancellationToken);

        var result = h.Completed[1];
        Assert.True(result.Copied);
        Assert.True(result.SaveFailed);
        Assert.Null(result.SavedPath);
        Assert.Equal(result, h.Sink.LastResult);                 // not the first capture's file
        Assert.All(first.Pixels, b => Assert.Equal(0, b));       // the previous image is gone
        Assert.Contains(second.Pixels, b => b != 0);             // the one on the clipboard can be re-copied
        h.Saver.Throw = false;
        Assert.True(await h.Sink.RecopyLastAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, h.Clipboard.Writes.Count);
    }

    [Fact]
    public async Task When_every_output_fails_the_previous_capture_stays_the_last_one()
    {
        var h = new Harness(new OutputSettings { DefaultAction = DefaultCaptureAction.CopyAndSave });
        var first = TestImages.Gradient(2, 2);
        await h.Sink.HandleAsync(first, CaptureRequest.Region, TestContext.Current.CancellationToken);
        var firstResult = h.Sink.LastResult;
        h.Clipboard.Throw = true;
        h.Saver.Throw = true;
        var second = TestImages.Gradient(3, 3);

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Sink.HandleAsync(second, CaptureRequest.Region, TestContext.Current.CancellationToken));

        Assert.All(second.Pixels, b => Assert.Equal(0, b));
        Assert.Contains(first.Pixels, b => b != 0);
        Assert.Equal(firstResult, h.Sink.LastResult);
        Assert.True(h.Completed[1].CopyFailed && h.Completed[1].SaveFailed);
    }

    [Fact]
    public async Task A_throwing_completed_handler_does_not_fail_the_capture_or_lose_the_image()
    {
        var h = new Harness(new OutputSettings { DefaultAction = DefaultCaptureAction.Copy });
        h.Sink.Completed += _ => throw new InvalidOperationException("subscriber bug");
        var image = TestImages.Gradient(2, 2);

        await h.Sink.HandleAsync(image, CaptureRequest.Region, TestContext.Current.CancellationToken);

        Assert.True(h.Sink.HasLastCapture);
        Assert.Contains(image.Pixels, b => b != 0);
        Assert.True(await h.Sink.RecopyLastAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cancellation_clears_the_image_and_reports_nothing()
    {
        var h = new Harness(new OutputSettings { DefaultAction = DefaultCaptureAction.Copy });
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var image = TestImages.Gradient(2, 2);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Sink.HandleAsync(image, CaptureRequest.Region, cancelled.Token));

        Assert.All(image.Pixels, b => Assert.Equal(0, b));
        Assert.Empty(h.Completed);
        Assert.False(h.Sink.HasLastCapture);
    }

    [Fact]
    public async Task A_new_capture_clears_the_previous_in_memory_image()
    {
        var h = new Harness(new OutputSettings { DefaultAction = DefaultCaptureAction.Copy });
        var first = TestImages.Gradient(2, 2);
        var second = TestImages.Gradient(3, 3);

        await h.Sink.HandleAsync(first, CaptureRequest.Region, TestContext.Current.CancellationToken);
        Assert.Contains(first.Pixels, b => b != 0);
        await h.Sink.HandleAsync(second, CaptureRequest.Region, TestContext.Current.CancellationToken);

        Assert.All(first.Pixels, b => Assert.Equal(0, b));
        Assert.Contains(second.Pixels, b => b != 0);
    }

    [Fact]
    public async Task Recopy_uses_the_last_image_and_reports_none_before_any_capture()
    {
        var h = new Harness(new OutputSettings { DefaultAction = DefaultCaptureAction.Save });

        Assert.False(await h.Sink.RecopyLastAsync(TestContext.Current.CancellationToken));
        await h.Sink.HandleAsync(TestImages.Gradient(2, 2), CaptureRequest.Region, TestContext.Current.CancellationToken);
        Assert.Empty(h.Clipboard.Writes);

        Assert.True(await h.Sink.RecopyLastAsync(TestContext.Current.CancellationToken));
        Assert.Single(h.Clipboard.Writes);
        Assert.Single(h.Completed); // recopy is not a new capture
    }

    [Fact]
    public async Task A_recopy_in_flight_holds_off_the_clearing_of_its_image()
    {
        var h = new Harness(new OutputSettings { DefaultAction = DefaultCaptureAction.Copy });
        var first = TestImages.Gradient(2, 2);
        await h.Sink.HandleAsync(first, CaptureRequest.Region, TestContext.Current.CancellationToken);

        // The recopy blocks inside the clipboard write; a new capture arrives meanwhile.
        using var inClipboard = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var pixelsSeenByRecopy = Array.Empty<byte>();
        h.Clipboard.OnWrite = () =>
        {
            if (h.Clipboard.Writes.Count == 1)
            {
                inClipboard.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                pixelsSeenByRecopy = first.Pixels.ToArray();
            }
        };

        var recopy = Task.Run(() => h.Sink.RecopyLastAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        Assert.True(inClipboard.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        var second = Task.Run(() => h.Sink.HandleAsync(TestImages.Gradient(3, 3), CaptureRequest.Region, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        await Task.Delay(150, TestContext.Current.CancellationToken); // give the new capture time to reach the gate
        release.Set();

        Assert.True(await recopy);
        await second;
        Assert.Contains(pixelsSeenByRecopy, b => b != 0);   // still intact while the recopy was using it
        Assert.All(first.Pixels, b => Assert.Equal(0, b));  // and cleared once it finished
    }

    [Fact]
    public async Task Dispose_clears_the_last_image_and_forgets_the_result()
    {
        var h = new Harness(new OutputSettings { DefaultAction = DefaultCaptureAction.Copy });
        var image = TestImages.Gradient(2, 2);
        await h.Sink.HandleAsync(image, CaptureRequest.Region, TestContext.Current.CancellationToken);

        h.Sink.Dispose();

        Assert.All(image.Pixels, b => Assert.Equal(0, b));
        Assert.False(h.Sink.HasLastCapture);
        Assert.False(await h.Sink.RecopyLastAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_capture_that_finishes_after_dispose_is_cleared_not_kept()
    {
        var h = new Harness(new OutputSettings { DefaultAction = DefaultCaptureAction.Copy });
        h.Sink.Dispose();
        var image = TestImages.Gradient(2, 2);

        await h.Sink.HandleAsync(image, CaptureRequest.Region, TestContext.Current.CancellationToken);

        Assert.All(image.Pixels, b => Assert.Equal(0, b));
        Assert.False(h.Sink.HasLastCapture);
    }

    [Theory]
    [InlineData(true, @"C:\Shots\a.png", false, false, @"Copied to the clipboard and saved to C:\Shots")]
    [InlineData(true, null, false, false, "Copied to the clipboard")]
    [InlineData(false, @"C:\Shots\a.png", false, false, @"Saved to C:\Shots")]
    [InlineData(true, null, false, true, "Copied to the clipboard. Saving the file failed; check the save folder in Settings.")]
    [InlineData(false, @"C:\Shots\a.png", true, false, @"Saved to C:\Shots. Copying to the clipboard failed.")]
    [InlineData(false, null, true, false, "Copying to the clipboard failed.")]
    [InlineData(false, null, false, true, "Saving the file failed; check the save folder in Settings.")]
    [InlineData(false, null, true, true, "The capture could not be copied or saved.")]
    public void Messages_name_the_folder_and_any_failed_output_but_never_the_file(bool copied, string? savedPath, bool copyFailed, bool saveFailed, string expected)
    {
        var message = OutputMessages.For(new OutputResult(copied, savedPath, ExcludedFromHistory: true, copyFailed, saveFailed));

        Assert.Equal(expected, message);
        Assert.DoesNotContain("a.png", message, StringComparison.Ordinal);
    }

    private sealed class Harness
    {
        public Harness(OutputSettings settings)
        {
            Sink = new OutputSink(Clipboard, Saver, () => settings);
            Sink.Completed += Completed.Add;
        }

        public FakeClipboard Clipboard { get; } = new();

        public FakeSaver Saver { get; } = new();

        public OutputSink Sink { get; }

        public List<OutputResult> Completed { get; } = [];
    }

    private sealed class FakeClipboard : IClipboardWriter
    {
        public List<(byte[] Png, byte[] Dib, bool Exclude)> Writes { get; } = [];

        public bool Throw { get; set; }

        public Action? OnWrite { get; set; }

        public void SetImage(ReadOnlySpan<byte> png, ReadOnlySpan<byte> dib, bool excludeFromHistory)
        {
            if (Throw)
            {
                throw new InvalidOperationException("clipboard busy");
            }

            OnWrite?.Invoke();
            Writes.Add((png.ToArray(), dib.ToArray(), excludeFromHistory));
        }
    }

    private sealed class FakeSaver : ICaptureSaver
    {
        public List<(byte[] Bytes, ReadOnlyMemory<byte> Original, OutputSettings Settings, DateTimeOffset CapturedAt)> Saves { get; } = [];

        public bool Throw { get; set; }

        public string PathFor(int n) => $@"X:\fake\{n}.img";

        public Task<string> SaveAsync(ReadOnlyMemory<byte> encoded, OutputSettings settings, DateTimeOffset capturedAt, CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new InvalidOperationException("disk full");
            }

            Saves.Add((encoded.ToArray(), encoded, settings, capturedAt));
            return Task.FromResult(PathFor(Saves.Count));
        }
    }
}
