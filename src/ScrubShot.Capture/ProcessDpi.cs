// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using Windows.Win32;
using Windows.Win32.UI.HiDpi;

namespace ScrubShot.Capture;

/// <summary>
/// Reports the DPI awareness the process actually received from its application manifest, and lets the
/// capture pipeline force PerMonitorV2 on the calling thread for the duration of a call.
/// </summary>
/// <remarks>
/// The capture pipeline treats every coordinate as a physical pixel, which is only true under the
/// PerMonitorV2 awareness context. The app checks this at startup and logs a warning if the manifest
/// did not take effect; the capture calls additionally set the context themselves so a thread that was
/// switched to another context can never produce a scaled capture.
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

    /// <summary>Switches the calling thread to PerMonitorV2 until the returned scope is disposed, which restores the previous context.</summary>
    internal static PerMonitorV2Scope EnterPerMonitorV2() => new();

    internal readonly struct PerMonitorV2Scope : IDisposable
    {
        private readonly DPI_AWARENESS_CONTEXT _previous;

        public PerMonitorV2Scope()
        {
            _previous = PInvoke.SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }

        public void Dispose()
        {
            if (!_previous.IsNull)
            {
                PInvoke.SetThreadDpiAwarenessContext(_previous);
            }
        }
    }
}
