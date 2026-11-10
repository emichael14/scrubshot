// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Geometry;

namespace ScrubShot.Capture.Overlay;

/// <summary>What the overlay asks the user to pick.</summary>
public enum OverlayMode
{
    /// <summary>Drag a rectangle; keyboard works too (Space anchors, arrows size, Enter confirms).</summary>
    Region,

    /// <summary>Hover highlights the window under the cursor; click or Enter confirms it.</summary>
    Window,
}

/// <summary>Presentation and behavior toggles, all settings-driven.</summary>
public sealed record OverlayOptions
{
    public static OverlayOptions Default { get; } = new();

    /// <summary>Show the pixel magnifier next to the cursor. M toggles it while the overlay is open.</summary>
    public bool ShowMagnifier { get; init; } = true;

    /// <summary>Show the cursor's virtual-screen coordinates and the selection size next to the cursor.</summary>
    public bool ShowCoordinates { get; init; } = true;

    /// <summary>Releasing the mouse after a drag confirms immediately. When false, release only selects and Enter confirms.</summary>
    public bool ConfirmOnRelease { get; init; } = true;

    /// <summary>A remembered region to pre-select (repeat last region). Ignored when it lies entirely off-screen.</summary>
    public PixelRect? InitialRegion { get; init; }
}

public enum OverlayOutcome
{
    Confirmed,
    Cancelled,
}

/// <summary>The user's choice: a region in virtual-screen pixels, and the window handle when a window was picked.</summary>
public sealed record OverlayResult(OverlayOutcome Outcome, PixelRect Region, nint WindowHandle = 0)
{
    public static OverlayResult Cancelled { get; } = new(OverlayOutcome.Cancelled, PixelRect.Empty);
}
