// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.ComponentModel;
using System.Runtime.InteropServices;
using ScrubShot.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ScrubShot.Capture;

/// <summary>
/// Captures the screen with GDI: one 32-bit top-down DIB section the size of the virtual screen, filled by
/// one <c>BitBlt</c> per monitor from the screen device context at physical-pixel coordinates (D-005).
/// <c>CAPTUREBLT</c> includes layered windows. DRM-protected content comes out black, which is acceptable.
/// The call forces PerMonitorV2 on the calling thread for its duration.
/// </summary>
public sealed class GdiScreenCapturer : IScreenCapturer
{
    public unsafe FrozenScreen Capture(MonitorLayout layout, CaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(options);

        using var dpiScope = ProcessDpi.EnterPerMonitorV2();

        // GetDC and CreateCompatibleDC are not documented to set a last-error code, so none is attached.
        var screenDc = PInvoke.GetDC(HWND.Null);
        if (screenDc.IsNull)
        {
            throw new Win32Exception("GetDC(NULL) failed.");
        }

        try
        {
            var memoryDc = PInvoke.CreateCompatibleDC(screenDc);
            if (memoryDc.IsNull)
            {
                throw new Win32Exception("CreateCompatibleDC failed.");
            }

            try
            {
                var frozen = FrozenScreen.Allocate(layout, DateTimeOffset.Now, options.IncludeCursor);
                try
                {
                    var previous = PInvoke.SelectObject(memoryDc, frozen.BitmapHandle);
                    if (previous.IsNull)
                    {
                        throw new Win32Exception("SelectObject could not select the capture bitmap.");
                    }

                    try
                    {
                        foreach (var monitor in layout.Monitors)
                        {
                            var dest = layout.ToBitmap(monitor.Bounds);
                            if (!PInvoke.BitBlt(memoryDc, dest.Left, dest.Top, dest.Width, dest.Height, screenDc, monitor.Bounds.Left, monitor.Bounds.Top, ROP_CODE.SRCCOPY | ROP_CODE.CAPTUREBLT))
                            {
                                throw new Win32Exception(Marshal.GetLastWin32Error(), "BitBlt failed.");
                            }
                        }

                        if (options.IncludeCursor)
                        {
                            DrawCursor(memoryDc, layout);
                        }

                        // Batched GDI calls report late failures only here.
                        if (!PInvoke.GdiFlush())
                        {
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "GdiFlush reported a failed GDI call.");
                        }
                    }
                    finally
                    {
                        PInvoke.SelectObject(memoryDc, previous);
                    }

                    frozen.SetOpaqueAlpha();
                }
                catch
                {
                    frozen.Dispose();
                    throw;
                }

                return frozen;
            }
            finally
            {
                PInvoke.DeleteDC(memoryDc);
            }
        }
        finally
        {
            PInvoke.ReleaseDC(HWND.Null, screenDc);
        }
    }

    /// <summary>
    /// Composites the visible cursor at its hotspot, at the cursor resource's own size. Silently skips when
    /// hidden, suppressed (touch or pen) or unavailable. Known limitation: on a monitor whose DPI differs from
    /// the one the cursor was loaded for, the drawn cursor may be a size step off (D-036).
    /// </summary>
    private static unsafe void DrawCursor(HDC memoryDc, MonitorLayout layout)
    {
        var cursor = new CURSORINFO { cbSize = (uint)sizeof(CURSORINFO) };
        if (!PInvoke.GetCursorInfo(ref cursor) || (cursor.flags & CURSORINFO_FLAGS.CURSOR_SHOWING) == 0 || cursor.hCursor.IsNull)
        {
            return;
        }

        ICONINFO iconInfo;
        if (!PInvoke.GetIconInfo(cursor.hCursor, &iconInfo))
        {
            return;
        }

        try
        {
            var position = layout.ToBitmap(new PixelPoint(cursor.ptScreenPos.X, cursor.ptScreenPos.Y));
            var left = position.X - (int)iconInfo.xHotspot;
            var top = position.Y - (int)iconInfo.yHotspot;
            PInvoke.DrawIconEx(memoryDc, left, top, cursor.hCursor, 0, 0, 0, HBRUSH.Null, DI_FLAGS.DI_NORMAL);
        }
        finally
        {
            // GetIconInfo allocates these; the caller must delete them.
            if (!iconInfo.hbmMask.IsNull)
            {
                PInvoke.DeleteObject(iconInfo.hbmMask);
            }

            if (!iconInfo.hbmColor.IsNull)
            {
                PInvoke.DeleteObject(iconInfo.hbmColor);
            }
        }
    }
}
