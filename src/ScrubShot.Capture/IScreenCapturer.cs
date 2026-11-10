// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Geometry;

namespace ScrubShot.Capture;

/// <summary>Options for one capture. The capture delay is the caller's concern; the capturer grabs the screen as it is now.</summary>
public sealed record CaptureOptions
{
    public static CaptureOptions Default { get; } = new();

    /// <summary>Composite the mouse cursor into the capture at its current position.</summary>
    public bool IncludeCursor { get; init; }
}

/// <summary>
/// Grabs every monitor into one <see cref="FrozenScreen"/>. Implementations: GDI (D-005) now,
/// Windows.Graphics.Capture later behind the same seam.
/// </summary>
public interface IScreenCapturer
{
    /// <summary>Captures the whole virtual screen described by <paramref name="layout"/>. The caller owns the result and must dispose it.</summary>
    FrozenScreen Capture(MonitorLayout layout, CaptureOptions options);
}
