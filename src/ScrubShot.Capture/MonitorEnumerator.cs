// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.ComponentModel;
using ScrubShot.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace ScrubShot.Capture;

/// <summary>
/// Reads the live display configuration into a <see cref="MonitorLayout"/>. The call runs under the
/// PerMonitorV2 context, so every rectangle is physical pixels in virtual-screen coordinates.
/// </summary>
public static class MonitorEnumerator
{
    public static unsafe MonitorLayout GetLayout()
    {
        using var dpiScope = ProcessDpi.EnterPerMonitorV2();

        var handles = new List<HMONITOR>();
        MONITORENUMPROC callback = (monitor, _, _, _) =>
        {
            handles.Add(monitor);
            return true;
        };

        // EnumDisplayMonitors is not documented to set a last-error code, so no code is attached.
        if (!PInvoke.EnumDisplayMonitors(HDC.Null, null, callback, default))
        {
            throw new Win32Exception("EnumDisplayMonitors failed.");
        }

        GC.KeepAlive(callback);

        var monitors = new List<MonitorInfo>(handles.Count);
        for (var i = 0; i < handles.Count; i++)
        {
            monitors.Add(Describe(handles[i], i));
        }

        return new MonitorLayout(monitors);
    }

    private static unsafe MonitorInfo Describe(HMONITOR monitor, int index)
    {
        var info = new MONITORINFOEXW();
        info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (!PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
        {
            throw new Win32Exception("GetMonitorInfo failed.");
        }

        var dpi = MonitorInfo.BaseDpi;
        if (PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Succeeded && dpiX > 0)
        {
            dpi = dpiX;
        }

        var bounds = ToPixelRect(info.monitorInfo.rcMonitor);
        var work = ToPixelRect(info.monitorInfo.rcWork);
        var isPrimary = (info.monitorInfo.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0;
        return new MonitorInfo(index, info.szDevice.ToString(), bounds, work, isPrimary, dpi);
    }

    private static PixelRect ToPixelRect(RECT rect) => PixelRect.FromEdges(rect.left, rect.top, rect.right, rect.bottom);
}
