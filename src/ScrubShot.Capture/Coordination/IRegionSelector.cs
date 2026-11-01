// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Capture.Overlay;

namespace ScrubShot.Capture.Coordination;

/// <summary>Asks the user for a region on a frozen screen. The overlay is the real implementation; tests substitute a scripted one.</summary>
public interface IRegionSelector
{
    Task<OverlayResult> SelectAsync(FrozenScreen frozen, OverlayMode mode, OverlayOptions options, CancellationToken cancellationToken);
}

/// <summary>Runs <see cref="OverlaySession"/> on its own STA thread.</summary>
public sealed class OverlayRegionSelector : IRegionSelector
{
    public static OverlayRegionSelector Instance { get; } = new();

    public Task<OverlayResult> SelectAsync(FrozenScreen frozen, OverlayMode mode, OverlayOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return OverlaySession.RunOnNewThreadAsync(frozen, mode, options);
    }
}
