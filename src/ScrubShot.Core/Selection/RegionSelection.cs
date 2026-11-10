// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Geometry;

namespace ScrubShot.Core.Selection;

public enum SelectionState
{
    /// <summary>No selection yet; the cursor shows a crosshair.</summary>
    Idle,

    /// <summary>The primary button is down and the selection follows the cursor.</summary>
    Dragging,

    /// <summary>A rectangle is chosen and waits for Enter, Space or a click to confirm.</summary>
    Selected,
}

/// <summary>
/// The region-select state machine shared by every overlay window, so a drag can cross monitors.
/// All points are physical pixels in virtual-screen coordinates and are clamped to the virtual screen.
/// The selection covers the pixels from the anchor to the cursor inclusive, so a drag from (10, 10)
/// to (12, 12) selects a 3x3 area and a click without movement selects nothing.
/// </summary>
public sealed class RegionSelection
{
    private PixelPoint _anchor;
    private PixelPoint _cursor;

    public RegionSelection(PixelRect bounds)
    {
        if (bounds.IsEmpty)
        {
            throw new ArgumentException("Selection bounds cannot be empty.", nameof(bounds));
        }

        Bounds = bounds;
        _cursor = bounds.Location;
    }

    /// <summary>The virtual screen. Nothing outside it can be selected.</summary>
    public PixelRect Bounds { get; }

    public SelectionState State { get; private set; } = SelectionState.Idle;

    /// <summary>Where the cursor is, clamped to <see cref="Bounds"/>. Drives the crosshair and magnifier.</summary>
    public PixelPoint Cursor => _cursor;

    /// <summary>The selection so far: empty while idle, live while dragging, final once selected.</summary>
    public PixelRect Rect { get; private set; } = PixelRect.Empty;

    /// <summary>Moves the cursor without changing the selection (idle hover, or hovering after a selection).</summary>
    public void MoveCursor(PixelPoint point)
    {
        _cursor = Bounds.ClampInclusive(point);
        if (State == SelectionState.Dragging)
        {
            Rect = Inclusive(_anchor, _cursor);
        }
    }

    public void BeginDrag(PixelPoint point)
    {
        _anchor = Bounds.ClampInclusive(point);
        _cursor = _anchor;
        Rect = Inclusive(_anchor, _cursor);
        State = SelectionState.Dragging;
    }

    /// <summary>Ends the drag. A drag that never moved leaves the model idle, so a stray click selects nothing.</summary>
    public void EndDrag(PixelPoint point)
    {
        if (State != SelectionState.Dragging)
        {
            return;
        }

        MoveCursor(point);
        if (_anchor == _cursor)
        {
            Rect = PixelRect.Empty;
            State = SelectionState.Idle;
            return;
        }

        State = SelectionState.Selected;
    }

    /// <summary>
    /// Moves the cursor corner of the selection by a delta (arrow keys). While dragging or selected the far
    /// corner moves; while idle the cursor itself moves so the crosshair can be positioned precisely.
    /// </summary>
    public void Nudge(int dx, int dy)
    {
        _cursor = Bounds.ClampInclusive(_cursor.Offset(dx, dy));
        if (State != SelectionState.Idle)
        {
            Rect = Inclusive(_anchor, _cursor);
        }
    }

    /// <summary>Replaces the selection outright (repeat last region, or a window pick).</summary>
    public bool Select(PixelRect rect)
    {
        var clamped = Bounds.Intersect(rect);
        if (clamped.IsEmpty)
        {
            return false;
        }

        _anchor = clamped.Location;
        _cursor = new PixelPoint(clamped.Right - 1, clamped.Bottom - 1);
        Rect = clamped;
        State = SelectionState.Selected;
        return true;
    }

    /// <summary>The final rectangle, or null when nothing is selected yet.</summary>
    public PixelRect? Confirm() => State == SelectionState.Selected && !Rect.IsEmpty ? Rect : null;

    public void Cancel()
    {
        Rect = PixelRect.Empty;
        State = SelectionState.Idle;
    }

    private static PixelRect Inclusive(PixelPoint a, PixelPoint b) =>
        PixelRect.FromEdges(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
}
