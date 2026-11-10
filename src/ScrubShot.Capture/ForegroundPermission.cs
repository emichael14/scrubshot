// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using Windows.Win32;
using Windows.Win32.Foundation;

namespace ScrubShot.Capture;

/// <summary>
/// Windows only lets certain processes bring a window to the foreground. A second launch that the user has just
/// started has that right; the instance already running in the tray does not. Before the second launch signals the
/// first, it passes the right on, so the Settings window can come forward instead of flashing in the taskbar.
/// </summary>
public static class ForegroundPermission
{
    /// <summary>Lets any process set the foreground window until the next user input. Returns false when this process may not do so itself.</summary>
    public static bool AllowAnyProcess() => PInvoke.AllowSetForegroundWindow(PInvoke.ASFW_ANY);

    /// <summary>
    /// Asks Windows to put the window in front. This is the call that uses the right a second launch passed on;
    /// activating the window alone restores and focuses it within this process but does not raise it above
    /// another application's windows. Returns false when Windows declines, in which case the taskbar button flashes.
    /// </summary>
    public static bool BringToForeground(nint windowHandle) =>
        windowHandle != 0 && PInvoke.SetForegroundWindow(new HWND(windowHandle));
}
