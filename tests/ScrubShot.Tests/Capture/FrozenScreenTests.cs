// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Capture;
using ScrubShot.Core.Geometry;

namespace ScrubShot.Tests.Capture;

/// <summary>Exercises the frozen screen on a real DIB section (no desktop needed) with a synthetic negative-origin layout.</summary>
public class FrozenScreenTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>8x4 virtual screen whose origin is (-2, -1): monitor 0 (primary) at (0,0) 6x3, monitor 1 at (-2,-1) 2x1.</summary>
    private static MonitorLayout Layout() => new(
    [
        new MonitorInfo(0, "A", new PixelRect(0, 0, 6, 3), new PixelRect(0, 0, 6, 3), true, 96),
        new MonitorInfo(1, "B", new PixelRect(-2, -1, 2, 1), new PixelRect(-2, -1, 2, 1), false, 144),
    ]);

    private static FrozenScreen Filled()
    {
        var frozen = FrozenScreen.Allocate(Layout(), Stamp, includesCursor: false);
        var pixels = frozen.WritablePixels;
        for (var y = 0; y < frozen.Height; y++)
        {
            for (var x = 0; x < frozen.Width; x++)
            {
                var i = (y * frozen.Stride) + (x * FrozenScreen.BytesPerPixel);
                pixels[i] = (byte)x;       // B = column
                pixels[i + 1] = (byte)y;   // G = row
                pixels[i + 2] = 7;         // R
                pixels[i + 3] = 0;         // alpha undefined, like GDI leaves it
            }
        }

        return frozen;
    }

    [Fact]
    public void Allocate_matches_the_virtual_screen()
    {
        using var frozen = FrozenScreen.Allocate(Layout(), Stamp, includesCursor: true);

        Assert.Equal(8, frozen.Width);
        Assert.Equal(4, frozen.Height);
        Assert.Equal(32, frozen.Stride);
        Assert.Equal(128, frozen.Pixels.Length);
        Assert.True(frozen.IncludesCursor);
        Assert.Equal(Stamp, frozen.CapturedAt);
        Assert.All(frozen.Pixels.ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Crop_clamps_to_the_bitmap_and_reports_the_screen_rect()
    {
        using var frozen = Filled();

        var image = frozen.Crop(new PixelRect(-2, -1, 5, 3));

        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(new PixelRect(-2, -1, 3, 2), image.ScreenRect);
        Assert.Equal(new byte[] { 0, 0, 7, 0, 1, 0, 7, 0, 2, 0, 7, 0 }, image.Pixels.AsSpan(0, 12).ToArray());
        Assert.Equal(1, image.Pixels[13]);
    }

    [Fact]
    public void Crop_of_an_interior_region_copies_the_right_rows()
    {
        using var frozen = Filled();

        var image = frozen.Crop(new PixelRect(5, 2, 10, 10));

        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(5, image.Pixels[0]);
        Assert.Equal(2, image.Pixels[1]);
        Assert.Equal(7, image.Pixels[(1 * image.Stride) + 4 + 1 - 1 + 2]);
        Assert.Equal(new PixelRect(3, 1, 3, 2), image.ScreenRect);
    }

    [Fact]
    public void Crop_outside_the_bitmap_throws()
    {
        using var frozen = Filled();

        Assert.Throws<ArgumentException>(() => frozen.Crop(new PixelRect(8, 0, 2, 2)));
        Assert.Throws<ArgumentException>(() => frozen.Crop(new PixelRect(-20, -20, 5, 5)));
    }

    [Fact]
    public void Set_opaque_alpha_touches_only_the_fourth_byte()
    {
        using var frozen = Filled();

        frozen.SetOpaqueAlpha();

        var pixels = frozen.Pixels;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            Assert.Equal(7, pixels[i + 2]);
            Assert.Equal(0xFF, pixels[i + 3]);
        }
    }

    [Fact]
    public void Dispose_is_idempotent_and_blocks_further_access()
    {
        var frozen = Filled();

        frozen.Dispose();
        frozen.Dispose();

        Assert.True(frozen.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => frozen.Pixels.Length);
        Assert.Throws<ObjectDisposedException>(() => frozen.Crop(frozen.Bounds));
    }

    [Fact]
    public void Captured_image_clear_zeroes_the_copy()
    {
        using var frozen = Filled();
        var image = frozen.Crop(frozen.Bounds);

        image.Clear();

        Assert.All(image.Pixels, b => Assert.Equal(0, b));
        Assert.NotEqual(0, frozen.Pixels[4]);
    }

    [Fact]
    public void Oversized_layout_is_rejected_before_allocation()
    {
        var huge = new MonitorLayout([new MonitorInfo(0, "X", new PixelRect(0, 0, 40_000, 40_000), new PixelRect(0, 0, 40_000, 40_000), true, 96)]);

        Assert.Throws<ArgumentException>(() => FrozenScreen.Allocate(huge, Stamp, includesCursor: false));
    }
}
