// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using Windows.Win32;
using Windows.Win32.UI.HiDpi;

namespace ScrubShot.Capture;

/// <summary>
/// Reports the DPI awareness the process actually received from its application manifest.
/// </summary>
/// <remarks>
/// The capture pipeline treats every coordinate as a physical pixel, which is only true under the
/// PerMonitorV2 awareness context. The app checks this at startup and logs a warning if the manifest
/// did not take effect, so a scaled capture is never a silent failure.
/// </remarks>
public static class ProcessDpi
{
    /// <summary>
    /// Returns <see langword="true"/> when the calling thread runs under the PerMonitorV2 DPI awareness context.
    /// </summary>
    public static bool IsPerMonitorV2()
    {
        var context = PInvoke.GetThreadDpiAwarenessContext();
        return PInvoke.AreDpiAwarenessContextsEqual(context, DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    }
}
