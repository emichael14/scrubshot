// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

namespace ScrubShot.Core.Geometry;

/// <summary>One display as the capture pipeline sees it: physical-pixel bounds in virtual-screen coordinates and its DPI.</summary>
/// <param name="Index">Position in the enumeration order, zero-based. Stable for the life of a layout.</param>
/// <param name="DeviceName">The GDI device name, for example <c>\\.\DISPLAY1</c>. Never shown to users as-is.</param>
/// <param name="Bounds">Monitor rectangle in physical pixels, virtual-screen coordinates. May have negative origin.</param>
/// <param name="WorkArea">The part of <paramref name="Bounds"/> not covered by the taskbar or app bars.</param>
/// <param name="IsPrimary">True for the monitor that contains the origin (0, 0).</param>
/// <param name="Dpi">Effective DPI; 96 means 100 percent scaling.</param>
public sealed record MonitorInfo(int Index, string DeviceName, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary, uint Dpi)
{
    public const uint BaseDpi = 96;

    /// <summary>Scale factor relative to 96 DPI, for example 1.5 for 150 percent.</summary>
    public double Scale => Dpi / (double)BaseDpi;
}

/// <summary>
/// The arrangement of all monitors and the virtual screen that bounds them. Everything is physical pixels.
/// Built by the capture layer from the live display configuration or by tests from synthetic data.
/// </summary>
public sealed class MonitorLayout
{
    public MonitorLayout(IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        monitors = monitors.ToArray(); // defensive copy: the invariants below must survive caller mutation
        if (monitors.Count == 0)
        {
            throw new ArgumentException("A layout needs at least one monitor.", nameof(monitors));
        }

        if (monitors.Count(m => m.IsPrimary) != 1)
        {
            throw new ArgumentException("A layout needs exactly one primary monitor.", nameof(monitors));
        }

        for (var i = 0; i < monitors.Count; i++)
        {
            if (monitors[i].Index != i)
            {
                throw new ArgumentException("Monitor indexes must match their position in the list.", nameof(monitors));
            }

            if (monitors[i].Bounds.IsEmpty)
            {
                throw new ArgumentException("Monitor bounds cannot be empty.", nameof(monitors));
            }
        }

        Monitors = monitors;
        Primary = monitors.Single(m => m.IsPrimary);
        VirtualScreen = monitors.Select(m => m.Bounds).Aggregate(PixelRect.Empty, (acc, r) => acc.Union(r));
    }

    public IReadOnlyList<MonitorInfo> Monitors { get; }

    public MonitorInfo Primary { get; }

    /// <summary>Bounding rectangle of all monitors in virtual-screen coordinates. Its origin is the bitmap origin of a frozen screen.</summary>
    public PixelRect VirtualScreen { get; }

    public bool HasMixedDpi => Monitors.Select(m => m.Dpi).Distinct().Count() > 1;

    /// <summary>The monitor containing <paramref name="point"/>, or null when it lies in a gap or outside every monitor.</summary>
    public MonitorInfo? MonitorAt(PixelPoint point) => Monitors.FirstOrDefault(m => m.Bounds.Contains(point));

    /// <summary>The monitor containing <paramref name="point"/>, or the nearest one when none does.</summary>
    public MonitorInfo NearestMonitor(PixelPoint point)
    {
        return MonitorAt(point) ?? Monitors.MinBy(m => DistanceSquared(m.Bounds, point))!;

        static long DistanceSquared(PixelRect rect, PixelPoint p)
        {
            long dx = p.X < rect.Left ? rect.Left - p.X : p.X >= rect.Right ? p.X - (rect.Right - 1) : 0;
            long dy = p.Y < rect.Top ? rect.Top - p.Y : p.Y >= rect.Bottom ? p.Y - (rect.Bottom - 1) : 0;
            return (dx * dx) + (dy * dy);
        }
    }

    /// <summary>Monitors whose bounds overlap <paramref name="rect"/>, in enumeration order.</summary>
    public IReadOnlyList<MonitorInfo> MonitorsIntersecting(PixelRect rect) => Monitors.Where(m => m.Bounds.IntersectsWith(rect)).ToList();

    /// <summary>Converts virtual-screen coordinates to coordinates in a bitmap whose (0, 0) is the virtual screen's top-left.</summary>
    public PixelRect ToBitmap(PixelRect virtualScreenRect) => virtualScreenRect.Translate(-VirtualScreen.Left, -VirtualScreen.Top);

    public PixelPoint ToBitmap(PixelPoint virtualScreenPoint) => virtualScreenPoint.Offset(-VirtualScreen.Left, -VirtualScreen.Top);

    /// <summary>Converts bitmap coordinates back to virtual-screen coordinates.</summary>
    public PixelRect ToVirtualScreen(PixelRect bitmapRect) => bitmapRect.Translate(VirtualScreen.Left, VirtualScreen.Top);

    public PixelPoint ToVirtualScreen(PixelPoint bitmapPoint) => bitmapPoint.Offset(VirtualScreen.Left, VirtualScreen.Top);

    /// <summary>Converts a virtual-screen point to coordinates relative to the monitor's own top-left.</summary>
    public static PixelPoint ToMonitorLocal(MonitorInfo monitor, PixelPoint virtualScreenPoint) =>
        virtualScreenPoint.Offset(-monitor.Bounds.Left, -monitor.Bounds.Top);

    /// <summary>Clamps a rectangle to the virtual screen. Returns an empty rectangle when there is no overlap.</summary>
    public PixelRect ClampToVirtualScreen(PixelRect rect) => VirtualScreen.Intersect(rect);
}
