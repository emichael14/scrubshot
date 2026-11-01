// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.ComponentModel;
using System.Runtime.InteropServices;
using ScrubShot.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace ScrubShot.Capture;

/// <summary>
/// The one unscrubbed copy of the screen: a 32-bit BGRA, top-down bitmap covering the whole virtual screen,
/// held in memory as a GDI DIB section. Bitmap (0, 0) is the virtual screen's top-left; use
/// <see cref="Layout"/> to convert. The alpha byte is always 0xFF (the capturer sets it; GDI leaves it
/// undefined). Disposing zeroes the pixels before releasing them, so the capture does not linger in freed
/// memory. Nothing here writes to disk.
/// </summary>
public sealed unsafe class FrozenScreen : IDisposable
{
    public const int BytesPerPixel = 4;

    /// <summary>Largest pixel count a frozen screen may hold (buffer size must fit an int).</summary>
    public const long MaxPixels = int.MaxValue / BytesPerPixel;

    private readonly int _byteLength;
    private HBITMAP _bitmap;
    private byte* _bits;

    private FrozenScreen(HBITMAP bitmap, byte* bits, MonitorLayout layout, DateTimeOffset capturedAt, bool includesCursor)
    {
        _bitmap = bitmap;
        _bits = bits;
        Layout = layout;
        CapturedAt = capturedAt;
        IncludesCursor = includesCursor;
        Width = layout.VirtualScreen.Width;
        Height = layout.VirtualScreen.Height;
        _byteLength = checked(Width * BytesPerPixel * Height);
    }

    ~FrozenScreen()
    {
        Release();
    }

    public MonitorLayout Layout { get; }

    public DateTimeOffset CapturedAt { get; }

    public bool IncludesCursor { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Bytes per row. Rows are tightly packed (32 bpp, so always a multiple of four).</summary>
    public int Stride => Width * BytesPerPixel;

    public PixelRect Bounds => new(0, 0, Width, Height);

    public bool IsDisposed => _bits is null;

    /// <summary>The GDI bitmap, for selecting into a memory DC (capture, overlay blits). Valid until <see cref="Dispose"/>.</summary>
    internal HBITMAP BitmapHandle => IsDisposed ? throw new ObjectDisposedException(nameof(FrozenScreen)) : _bitmap;

    /// <summary>
    /// Direct read access to the BGRA pixels, top-down, row stride <see cref="Stride"/>. The span is valid only
    /// while this object is alive: never hold it across <see cref="Dispose"/>.
    /// </summary>
    public ReadOnlySpan<byte> Pixels
    {
        get
        {
            var bits = _bits;
            ObjectDisposedException.ThrowIf(bits is null, this);
            return new ReadOnlySpan<byte>(bits, _byteLength);
        }
    }

    /// <summary>Writable view for the capturer and tests. Same lifetime rule as <see cref="Pixels"/>.</summary>
    internal Span<byte> WritablePixels
    {
        get
        {
            var bits = _bits;
            ObjectDisposedException.ThrowIf(bits is null, this);
            return new Span<byte>(bits, _byteLength);
        }
    }

    /// <summary>
    /// Allocates an empty (zeroed) frozen screen for <paramref name="layout"/> as a top-down 32-bit DIB section.
    /// No device context is needed, so this works on any thread and without a desktop.
    /// </summary>
    internal static FrozenScreen Allocate(MonitorLayout layout, DateTimeOffset capturedAt, bool includesCursor)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var size = layout.VirtualScreen;
        if ((long)size.Width * size.Height > MaxPixels)
        {
            throw new ArgumentException($"The virtual screen {size} is too large to capture.", nameof(layout));
        }

        var info = new BITMAPINFO();
        info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = size.Width;
        info.bmiHeader.biHeight = -size.Height; // negative: top-down rows
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = (uint)BI_COMPRESSION.BI_RGB;

        void* bits;
        var bitmap = PInvoke.CreateDIBSection(HDC.Null, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
        if (bitmap.IsNull || bits is null)
        {
            var error = Marshal.GetLastWin32Error();
            if (!bitmap.IsNull)
            {
                PInvoke.DeleteObject(bitmap);
            }

            throw new Win32Exception(error, "CreateDIBSection failed.");
        }

        return new FrozenScreen(bitmap, (byte*)bits, layout, capturedAt, includesCursor);
    }

    /// <summary>Sets the alpha byte of every pixel to 0xFF. GDI leaves the fourth byte of a 32-bit BI_RGB surface undefined.</summary>
    internal void SetOpaqueAlpha()
    {
        var pixels = MemoryMarshal.Cast<byte, uint>(WritablePixels);
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] |= 0xFF000000u;
        }
    }

    /// <summary>Copies a region (bitmap coordinates) into a new tightly packed BGRA buffer.</summary>
    public CapturedImage Crop(PixelRect bitmapRect)
    {
        var source = Pixels;
        var region = Bounds.Intersect(bitmapRect);
        if (region.IsEmpty)
        {
            throw new ArgumentException("The region does not overlap the captured screen.", nameof(bitmapRect));
        }

        var rowBytes = region.Width * BytesPerPixel;
        var buffer = new byte[checked(rowBytes * region.Height)];
        for (var row = 0; row < region.Height; row++)
        {
            var sourceOffset = ((region.Top + row) * Stride) + (region.Left * BytesPerPixel);
            source.Slice(sourceOffset, rowBytes).CopyTo(buffer.AsSpan(row * rowBytes, rowBytes));
        }

        return new CapturedImage(buffer, region.Width, region.Height, Layout.ToVirtualScreen(region), CapturedAt);
    }

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    private void Release()
    {
        var bits = _bits;
        if (bits is not null)
        {
            _bits = null;
            new Span<byte>(bits, _byteLength).Clear();
        }

        if (!_bitmap.IsNull)
        {
            PInvoke.DeleteObject(_bitmap);
            _bitmap = HBITMAP.Null;
        }
    }
}

/// <summary>A cropped capture: tightly packed BGRA pixels (alpha 0xFF) in managed memory, plus where on the virtual screen they came from.</summary>
public sealed class CapturedImage
{
    internal CapturedImage(byte[] pixels, int width, int height, PixelRect screenRect, DateTimeOffset capturedAt)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
        ScreenRect = screenRect;
        CapturedAt = capturedAt;
    }

    /// <summary>BGRA, top-down, stride = Width * 4.</summary>
    public byte[] Pixels { get; }

    public int Width { get; }

    public int Height { get; }

    public int Stride => Width * FrozenScreen.BytesPerPixel;

    /// <summary>The region in virtual-screen coordinates (physical pixels).</summary>
    public PixelRect ScreenRect { get; }

    public DateTimeOffset CapturedAt { get; }

    /// <summary>Zeroes the pixel buffer. Call when the image leaves the pipeline without being kept.</summary>
    public void Clear() => Array.Clear(Pixels);
}
