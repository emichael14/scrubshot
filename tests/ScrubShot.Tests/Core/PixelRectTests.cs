// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Geometry;

namespace ScrubShot.Tests.Core;

public class PixelRectTests
{
    [Fact]
    public void Edges_are_exclusive()
    {
        var rect = new PixelRect(10, 20, 30, 40);

        Assert.Equal(40, rect.Right);
        Assert.Equal(60, rect.Bottom);
        Assert.True(rect.Contains(new PixelPoint(39, 59)));
        Assert.False(rect.Contains(new PixelPoint(40, 59)));
        Assert.False(rect.Contains(new PixelPoint(39, 60)));
    }

    [Fact]
    public void From_points_normalizes_any_drag_direction()
    {
        var a = new PixelPoint(100, 200);
        var b = new PixelPoint(-50, 20);

        var forward = PixelRect.FromPoints(b, a);
        var backward = PixelRect.FromPoints(a, b);

        Assert.Equal(forward, backward);
        Assert.Equal(PixelRect.FromEdges(-50, 20, 100, 200), forward);
        Assert.True(PixelRect.FromPoints(a, a).IsEmpty);
    }

    [Fact]
    public void Negative_sizes_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelRect(0, 0, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PixelRect.FromEdges(10, 0, 5, 0));
    }

    [Fact]
    public void Intersect_and_union_handle_negative_coordinates()
    {
        var left = new PixelRect(-1920, 0, 1920, 1080);
        var right = new PixelRect(0, -200, 2560, 1440);

        Assert.Equal(PixelRect.Empty, left.Intersect(right));
        Assert.False(left.IntersectsWith(right));
        Assert.Equal(PixelRect.FromEdges(-1920, -200, 2560, 1240), left.Union(right));

        var overlap = new PixelRect(-100, 100, 300, 300);
        Assert.Equal(PixelRect.FromEdges(-100, 100, 0, 400), left.Intersect(overlap));
        Assert.Equal(PixelRect.FromEdges(0, 100, 200, 400), right.Intersect(overlap));
    }

    [Fact]
    public void Empty_rectangles_never_intersect_and_are_neutral_in_union()
    {
        var rect = new PixelRect(5, 5, 10, 10);

        Assert.False(rect.IntersectsWith(PixelRect.Empty));
        Assert.Equal(rect, rect.Union(PixelRect.Empty));
        Assert.Equal(rect, PixelRect.Empty.Union(rect));
        Assert.True(rect.Contains(PixelRect.Empty));
    }

    [Fact]
    public void Clamp_inclusive_keeps_a_point_on_the_last_pixel()
    {
        var rect = new PixelRect(0, 0, 100, 50);

        Assert.Equal(new PixelPoint(99, 49), rect.ClampInclusive(new PixelPoint(500, 500)));
        Assert.Equal(new PixelPoint(0, 0), rect.ClampInclusive(new PixelPoint(-5, -5)));
        Assert.Equal(new PixelPoint(10, 10), rect.ClampInclusive(new PixelPoint(10, 10)));
    }

    [Fact]
    public void Translate_moves_without_resizing()
    {
        var rect = new PixelRect(10, 10, 20, 20);

        var moved = rect.Translate(-30, 5);

        Assert.Equal(new PixelRect(-20, 15, 20, 20), moved);
        Assert.Equal(rect.Size, moved.Size);
    }
}
