// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Geometry;

namespace ScrubShot.Core.Selection;

/// <summary>A top-level window as a pick candidate: its visible frame in virtual-screen pixels and an opaque handle.</summary>
/// <param name="Handle">The native window handle, carried as an integer so Core stays free of Win32 types.</param>
/// <param name="Frame">The extended frame bounds (what the user sees, without the invisible resize border).</param>
public sealed record WindowCandidate(nint Handle, PixelRect Frame);

/// <summary>
/// Hit-tests the cursor against top-level windows in z-order (front to back) and clamps the chosen frame to
/// the virtual screen, so a window hanging off the edge yields only its visible part.
/// </summary>
public static class WindowPick
{
    /// <summary>The frontmost candidate containing <paramref name="point"/>, or null when the cursor is over the desktop or a gap.</summary>
    public static WindowCandidate? HitTest(IReadOnlyList<WindowCandidate> zOrdered, PixelPoint point)
    {
        ArgumentNullException.ThrowIfNull(zOrdered);
        return zOrdered.FirstOrDefault(c => c.Frame.Contains(point));
    }

    /// <summary>The part of a window frame that can be captured, or empty when it lies entirely off-screen.</summary>
    public static PixelRect CapturableFrame(WindowCandidate candidate, PixelRect virtualScreen)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return virtualScreen.Intersect(candidate.Frame);
    }
}

/// <summary>Reconciles a remembered region with the current display layout for "repeat last region".</summary>
public static class LastRegion
{
    /// <summary>
    /// Returns the remembered region clamped to the current virtual screen, or null when none of it is still
    /// on screen (for example after a monitor was unplugged), so the caller can fall back to region select.
    /// </summary>
    public static PixelRect? Reconcile(PixelRect remembered, PixelRect virtualScreen)
    {
        var clamped = virtualScreen.Intersect(remembered);
        return clamped.IsEmpty ? null : clamped;
    }
}
