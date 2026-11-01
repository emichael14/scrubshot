// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Geometry;

namespace ScrubShot.Tests.Core;

/// <summary>Synthetic layouts standing in for the mixed-DPI hardware the acceptance test needs.</summary>
public class MonitorLayoutTests
{
    private static MonitorInfo Monitor(int index, int left, int top, int width, int height, uint dpi, bool primary = false, string name = "") =>
        new(index, name.Length == 0 ? $@"\\.\DISPLAY{index + 1}" : name, new PixelRect(left, top, width, height), new PixelRect(left, top, width, height - 48), primary, dpi);

    /// <summary>150 percent laptop (1920x1080 physical) on the left, 100 percent 2560x1440 monitor on the right, laptop primary.</summary>
    private static MonitorLayout LaptopLeftOfMonitor() => new(
    [
        Monitor(0, 0, 0, 1920, 1080, 144, primary: true),
        Monitor(1, 1920, 0, 2560, 1440, 96),
    ]);

    /// <summary>Secondary 100 percent monitor placed left of a 150 percent primary, so the virtual screen starts at a negative x.</summary>
    private static MonitorLayout SecondaryLeftWithNegativeOrigin() => new(
    [
        Monitor(0, 0, 0, 1920, 1080, 144, primary: true),
        Monitor(1, -2560, -360, 2560, 1440, 96),
    ]);

    /// <summary>Secondary stacked above the primary.</summary>
    private static MonitorLayout StackedAbove() => new(
    [
        Monitor(0, 0, 0, 2560, 1440, 96, primary: true),
        Monitor(1, 320, -1080, 1920, 1080, 120),
    ]);

    /// <summary>Three monitors, primary in the middle, enumeration order not spatial order (index 2 is leftmost).</summary>
    private static MonitorLayout PrimaryInMiddle() => new(
    [
        Monitor(0, 1920, 0, 1920, 1080, 120),
        Monitor(1, 0, 0, 1920, 1080, 144, primary: true),
        Monitor(2, -2560, 0, 2560, 1440, 96),
    ]);

    [Fact]
    public void Primary_need_not_be_first_and_order_is_enumeration_order()
    {
        var layout = PrimaryInMiddle();

        Assert.Equal(1, layout.Primary.Index);
        Assert.Equal(PixelRect.FromEdges(-2560, 0, 3840, 1440), layout.VirtualScreen);
        Assert.True(layout.HasMixedDpi);
        Assert.Equal([0, 1, 2], layout.MonitorsIntersecting(new PixelRect(-100, 10, 2200, 10)).Select(m => m.Index));
        Assert.Equal(2, layout.MonitorAt(new PixelPoint(-1, 0))!.Index);
    }

    [Fact]
    public void Nearest_monitor_distance_is_symmetric_around_exclusive_edges()
    {
        var layout = new MonitorLayout(
        [
            Monitor(0, 0, 0, 100, 100, 96, primary: true),
            Monitor(1, 300, 0, 100, 100, 96),
        ]);

        Assert.Equal(0, layout.NearestMonitor(new PixelPoint(150, 50)).Index);
        Assert.Equal(1, layout.NearestMonitor(new PixelPoint(250, 50)).Index);
        Assert.Equal(0, layout.NearestMonitor(new PixelPoint(-1, 50)).Index);
        Assert.Equal(0, layout.NearestMonitor(new PixelPoint(100, 50)).Index);
    }

    [Fact]
    public void Layout_is_unaffected_by_later_mutation_of_the_source_list()
    {
        var list = new List<MonitorInfo> { Monitor(0, 0, 0, 100, 100, 96, primary: true) };
        var layout = new MonitorLayout(list);

        list.Add(Monitor(1, 100, 0, 100, 100, 96));
        list.Clear();

        Assert.Single(layout.Monitors);
        Assert.Equal(new PixelRect(0, 0, 100, 100), layout.VirtualScreen);
    }

    [Fact]
    public void Stacked_layout_has_a_gap_beside_the_upper_monitor()
    {
        var layout = StackedAbove();

        Assert.Null(layout.MonitorAt(new PixelPoint(100, -10)));
        Assert.Equal(1, layout.MonitorAt(new PixelPoint(320, -1))!.Index);
        Assert.False(layout.ClampToVirtualScreen(new PixelRect(10, -500, 50, 50)).IsEmpty);
    }

    [Fact]
    public void Virtual_screen_is_the_union_of_all_monitors()
    {
        Assert.Equal(new PixelRect(0, 0, 4480, 1440), LaptopLeftOfMonitor().VirtualScreen);
        Assert.Equal(PixelRect.FromEdges(-2560, -360, 1920, 1080), SecondaryLeftWithNegativeOrigin().VirtualScreen);
        Assert.Equal(PixelRect.FromEdges(0, -1080, 2560, 1440), StackedAbove().VirtualScreen);
    }

    [Fact]
    public void Primary_contains_the_origin_and_mixed_dpi_is_detected()
    {
        var layout = SecondaryLeftWithNegativeOrigin();

        Assert.Equal(0, layout.Primary.Index);
        Assert.True(layout.Primary.Bounds.Contains(PixelPoint.Zero));
        Assert.True(layout.HasMixedDpi);
        Assert.Equal(1.5, layout.Primary.Scale);
        Assert.False(new MonitorLayout([Monitor(0, 0, 0, 1920, 1080, 96, primary: true)]).HasMixedDpi);
    }

    [Fact]
    public void Bitmap_coordinates_start_at_the_virtual_screen_origin()
    {
        var layout = SecondaryLeftWithNegativeOrigin();

        Assert.Equal(new PixelRect(0, 0, 2560, 1440), layout.ToBitmap(layout.Monitors[1].Bounds));
        Assert.Equal(new PixelRect(2560, 360, 1920, 1080), layout.ToBitmap(layout.Monitors[0].Bounds));
        Assert.Equal(new PixelPoint(2560, 360), layout.ToBitmap(PixelPoint.Zero));
        Assert.Equal(PixelPoint.Zero, layout.ToVirtualScreen(new PixelPoint(2560, 360)));

        var roundTrip = layout.ToVirtualScreen(layout.ToBitmap(new PixelRect(-100, -100, 50, 50)));
        Assert.Equal(new PixelRect(-100, -100, 50, 50), roundTrip);
    }

    [Fact]
    public void Every_monitor_maps_inside_the_bitmap()
    {
        foreach (var layout in new[] { LaptopLeftOfMonitor(), SecondaryLeftWithNegativeOrigin(), StackedAbove() })
        {
            var bitmap = new PixelRect(0, 0, layout.VirtualScreen.Width, layout.VirtualScreen.Height);
            Assert.All(layout.Monitors, m => Assert.True(bitmap.Contains(layout.ToBitmap(m.Bounds))));
        }
    }

    [Fact]
    public void Monitor_at_point_respects_exclusive_edges_and_gaps()
    {
        var layout = SecondaryLeftWithNegativeOrigin();

        Assert.Equal(1, layout.MonitorAt(new PixelPoint(-1, 0))!.Index);
        Assert.Equal(0, layout.MonitorAt(new PixelPoint(0, 0))!.Index);
        Assert.Null(layout.MonitorAt(new PixelPoint(-1, 1080)));
        Assert.Null(layout.MonitorAt(new PixelPoint(1920, 0)));
    }

    [Fact]
    public void Nearest_monitor_picks_the_closest_edge_in_a_gap()
    {
        var layout = SecondaryLeftWithNegativeOrigin();

        Assert.Equal(1, layout.NearestMonitor(new PixelPoint(-500, 1090)).Index);
        Assert.Equal(0, layout.NearestMonitor(new PixelPoint(500, -500)).Index);
        Assert.Equal(0, layout.NearestMonitor(new PixelPoint(10, 10)).Index);
    }

    [Fact]
    public void Monitors_intersecting_a_rectangle_that_spans_the_dpi_boundary()
    {
        var layout = LaptopLeftOfMonitor();

        var spanning = layout.MonitorsIntersecting(new PixelRect(1800, 100, 300, 300));
        var single = layout.MonitorsIntersecting(new PixelRect(100, 100, 300, 300));
        var outside = layout.MonitorsIntersecting(new PixelRect(100, 1100, 300, 300));

        Assert.Equal([0, 1], spanning.Select(m => m.Index));
        Assert.Equal([0], single.Select(m => m.Index));
        Assert.Empty(outside);
    }

    [Fact]
    public void Monitor_local_coordinates_are_relative_to_the_monitor_origin()
    {
        var layout = SecondaryLeftWithNegativeOrigin();

        Assert.Equal(new PixelPoint(10, 20), MonitorLayout.ToMonitorLocal(layout.Monitors[1], new PixelPoint(-2550, -340)));
    }

    [Fact]
    public void Clamp_to_virtual_screen_trims_or_empties()
    {
        var layout = StackedAbove();

        Assert.Equal(PixelRect.FromEdges(0, -1080, 100, 100), layout.ClampToVirtualScreen(new PixelRect(-50, -2000, 150, 2100)));
        Assert.True(layout.ClampToVirtualScreen(new PixelRect(3000, 0, 10, 10)).IsEmpty);
    }

    [Fact]
    public void Invalid_layouts_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new MonitorLayout([]));
        Assert.Throws<ArgumentException>(() => new MonitorLayout([Monitor(0, 0, 0, 100, 100, 96)]));
        Assert.Throws<ArgumentException>(() => new MonitorLayout([Monitor(0, 0, 0, 100, 100, 96, primary: true), Monitor(1, 100, 0, 100, 100, 96, primary: true)]));
        Assert.Throws<ArgumentException>(() => new MonitorLayout([Monitor(1, 0, 0, 100, 100, 96, primary: true)]));
        Assert.Throws<ArgumentException>(() => new MonitorLayout([new MonitorInfo(0, "x", PixelRect.Empty, PixelRect.Empty, true, 96)]));
    }
}
