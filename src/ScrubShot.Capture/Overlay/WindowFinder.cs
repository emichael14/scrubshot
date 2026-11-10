// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Geometry;
using ScrubShot.Core.Selection;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ScrubShot.Capture.Overlay;

/// <summary>
/// Lists the top-level windows a user can pick, front to back, with their extended frame bounds
/// (what is visible on screen, without the invisible resize border). Cloaked, minimized, invisible,
/// tool and shell windows are skipped, as are windows created by the calling thread (the overlay itself).
/// </summary>
public static class WindowFinder
{
    public static unsafe IReadOnlyList<WindowCandidate> GetCandidates()
    {
        var candidates = new List<WindowCandidate>();
        var ownThread = PInvoke.GetCurrentThreadId();
        var shell = PInvoke.GetShellWindow();

        WNDENUMPROC callback = (hwnd, _) =>
        {
            if (hwnd == shell || !PInvoke.IsWindowVisible(hwnd) || PInvoke.IsIconic(hwnd))
            {
                return true;
            }

            if (PInvoke.GetWindowThreadProcessId(hwnd, null) == ownThread)
            {
                return true;
            }

            // Tool windows have no taskbar presence; click-through (transparent) windows are overlays the user cannot see as windows.
            var exStyle = (WINDOW_EX_STYLE)(uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
            if ((exStyle & (WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_TRANSPARENT)) != 0)
            {
                return true;
            }

            uint cloaked;
            if (PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, (uint)sizeof(uint)).Succeeded && cloaked != 0)
            {
                return true;
            }

            if (IsDesktopShellClass(hwnd))
            {
                return true;
            }

            RECT frame;
            if (PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &frame, (uint)sizeof(RECT)).Failed)
            {
                return true;
            }

            if (frame.right <= frame.left || frame.bottom <= frame.top)
            {
                return true;
            }

            candidates.Add(new WindowCandidate((nint)hwnd.Value, PixelRect.FromEdges(frame.left, frame.top, frame.right, frame.bottom)));
            return true;
        };

        PInvoke.EnumWindows(callback, default);
        GC.KeepAlive(callback);
        return candidates;
    }

    /// <summary>The desktop background windows, which would otherwise match every point.</summary>
    private static unsafe bool IsDesktopShellClass(HWND hwnd)
    {
        Span<char> buffer = stackalloc char[32];
        var length = PInvoke.GetClassName(hwnd, buffer);
        if (length <= 0)
        {
            return false;
        }

        var name = new string(buffer[..length]);
        return name is "Progman" or "WorkerW";
    }
}
