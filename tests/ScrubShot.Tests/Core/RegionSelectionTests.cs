// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Geometry;
using ScrubShot.Core.Selection;

namespace ScrubShot.Tests.Core;

public class RegionSelectionTests
{
    /// <summary>A virtual screen with a negative origin: 100 percent monitor left of a primary at (0, 0).</summary>
    private static readonly PixelRect Screen = PixelRect.FromEdges(-2560, -360, 1920, 1080);

    [Fact]
    public void Starts_idle_with_an_empty_rect()
    {
        var model = new RegionSelection(Screen);

        Assert.Equal(SelectionState.Idle, model.State);
        Assert.True(model.Rect.IsEmpty);
        Assert.Null(model.Confirm());
    }

    [Theory]
    [InlineData(10, 10, 12, 12)]
    [InlineData(12, 12, 10, 10)]
    [InlineData(12, 10, 10, 12)]
    [InlineData(10, 12, 12, 10)]
    public void Drag_in_any_direction_selects_the_inclusive_rectangle(int ax, int ay, int bx, int by)
    {
        var model = new RegionSelection(Screen);

        model.BeginDrag(new PixelPoint(ax, ay));
        model.MoveCursor(new PixelPoint(bx, by));
        Assert.Equal(SelectionState.Dragging, model.State);
        model.EndDrag(new PixelPoint(bx, by));

        Assert.Equal(SelectionState.Selected, model.State);
        Assert.Equal(new PixelRect(10, 10, 3, 3), model.Rect);
        Assert.Equal(new PixelRect(10, 10, 3, 3), model.Confirm());
    }

    [Fact]
    public void Drag_across_the_monitor_boundary_and_negative_coordinates()
    {
        var model = new RegionSelection(Screen);

        model.BeginDrag(new PixelPoint(-100, -50));
        model.EndDrag(new PixelPoint(99, 49));

        Assert.Equal(PixelRect.FromEdges(-100, -50, 100, 50), model.Rect);
    }

    [Fact]
    public void Points_outside_the_screen_are_clamped()
    {
        var model = new RegionSelection(Screen);

        model.BeginDrag(new PixelPoint(-9999, -9999));
        model.EndDrag(new PixelPoint(9999, 9999));

        Assert.Equal(Screen, model.Rect);
        Assert.Equal(new PixelPoint(1919, 1079), model.Cursor);
    }

    [Fact]
    public void Click_without_movement_selects_nothing()
    {
        var model = new RegionSelection(Screen);

        model.BeginDrag(new PixelPoint(5, 5));
        model.EndDrag(new PixelPoint(5, 5));

        Assert.Equal(SelectionState.Idle, model.State);
        Assert.True(model.Rect.IsEmpty);
        Assert.Null(model.Confirm());
    }

    [Fact]
    public void Dragging_is_not_confirmable_until_released()
    {
        var model = new RegionSelection(Screen);

        model.BeginDrag(new PixelPoint(0, 0));
        model.MoveCursor(new PixelPoint(50, 50));

        Assert.Null(model.Confirm());
        Assert.Equal(new PixelRect(0, 0, 51, 51), model.Rect);
    }

    [Fact]
    public void Nudge_moves_the_cursor_corner_after_selection_and_the_crosshair_when_idle()
    {
        var model = new RegionSelection(Screen);
        model.MoveCursor(new PixelPoint(100, 100));

        model.Nudge(5, -5);
        Assert.Equal(new PixelPoint(105, 95), model.Cursor);
        Assert.True(model.Rect.IsEmpty);

        model.BeginDrag(new PixelPoint(0, 0));
        model.EndDrag(new PixelPoint(10, 10));
        model.Nudge(1, 0);
        model.Nudge(0, -2);

        Assert.Equal(new PixelRect(0, 0, 12, 9), model.Rect);
        Assert.Equal(SelectionState.Selected, model.State);
    }

    [Fact]
    public void Nudge_is_clamped_to_the_screen()
    {
        var model = new RegionSelection(Screen);
        model.BeginDrag(new PixelPoint(1900, 1070));
        model.EndDrag(new PixelPoint(1910, 1075));

        model.Nudge(100, 100);

        Assert.Equal(PixelRect.FromEdges(1900, 1070, 1920, 1080), model.Rect);
    }

    [Fact]
    public void Select_replaces_the_selection_and_clamps_it()
    {
        var model = new RegionSelection(Screen);

        Assert.True(model.Select(new PixelRect(1800, 1000, 500, 500)));
        Assert.Equal(PixelRect.FromEdges(1800, 1000, 1920, 1080), model.Rect);
        Assert.Equal(SelectionState.Selected, model.State);
        Assert.Equal(new PixelPoint(1919, 1079), model.Cursor);

        Assert.False(model.Select(new PixelRect(5000, 5000, 10, 10)));
    }

    [Fact]
    public void Cancel_returns_to_idle()
    {
        var model = new RegionSelection(Screen);
        model.BeginDrag(new PixelPoint(0, 0));
        model.EndDrag(new PixelPoint(10, 10));

        model.Cancel();

        Assert.Equal(SelectionState.Idle, model.State);
        Assert.Null(model.Confirm());
    }

    [Fact]
    public void A_new_drag_after_a_selection_starts_over()
    {
        var model = new RegionSelection(Screen);
        model.BeginDrag(new PixelPoint(0, 0));
        model.EndDrag(new PixelPoint(10, 10));

        model.BeginDrag(new PixelPoint(100, 100));
        model.EndDrag(new PixelPoint(101, 101));

        Assert.Equal(new PixelRect(100, 100, 2, 2), model.Rect);
    }

    [Fact]
    public void Empty_bounds_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new RegionSelection(PixelRect.Empty));
    }
}

public class WindowPickTests
{
    private static readonly PixelRect Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void Frontmost_window_under_the_cursor_wins()
    {
        var front = new WindowCandidate(1, new PixelRect(100, 100, 300, 300));
        var back = new WindowCandidate(2, new PixelRect(0, 0, 800, 800));

        Assert.Equal(front, WindowPick.HitTest([front, back], new PixelPoint(150, 150)));
        Assert.Equal(back, WindowPick.HitTest([front, back], new PixelPoint(500, 500)));
        Assert.Null(WindowPick.HitTest([front, back], new PixelPoint(900, 900)));
        Assert.Null(WindowPick.HitTest([], new PixelPoint(0, 0)));
    }

    [Fact]
    public void Capturable_frame_is_clipped_to_the_virtual_screen()
    {
        var hangingOff = new WindowCandidate(1, new PixelRect(1800, -50, 400, 200));

        Assert.Equal(PixelRect.FromEdges(1800, 0, 1920, 150), WindowPick.CapturableFrame(hangingOff, Screen));
        Assert.True(WindowPick.CapturableFrame(new WindowCandidate(2, new PixelRect(-500, -500, 10, 10)), Screen).IsEmpty);
    }

    [Fact]
    public void Last_region_is_clamped_or_dropped_after_a_layout_change()
    {
        Assert.Equal(new PixelRect(100, 100, 50, 50), LastRegion.Reconcile(new PixelRect(100, 100, 50, 50), Screen));
        Assert.Equal(PixelRect.FromEdges(1900, 0, 1920, 100), LastRegion.Reconcile(new PixelRect(1900, -100, 500, 200), Screen));
        Assert.Null(LastRegion.Reconcile(new PixelRect(-2000, 0, 500, 500), Screen));
    }
}
