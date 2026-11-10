// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScrubShot.Capture.Overlay;
using ScrubShot.Core.Coordination;
using ScrubShot.Core.Geometry;
using ScrubShot.Core.Logging;
using ScrubShot.Core.Selection;
using ScrubShot.Core.Settings;

namespace ScrubShot.Capture.Coordination;

/// <summary>
/// Ties a capture request to the pipeline: optional delay, freeze the screen, pick the region (overlay or implied),
/// crop, hand the result to the sink. One capture runs at a time; a request that arrives while one is in flight is
/// dropped with <see cref="CaptureOutcome.Busy"/>. The frozen screen is disposed on every path before the sink runs,
/// so the only copy of the full screen never outlives the selection (D-040).
/// </summary>
public sealed class CaptureCoordinator
{
    private readonly IScreenCapturer _capturer;
    private readonly Func<MonitorLayout> _layoutProvider;
    private readonly IRegionSelector _selector;
    private readonly ICaptureSink _sink;
    private readonly Func<CaptureSettings> _settingsProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Lock _lastRegionLock = new();
    private PixelRect? _lastRegion;
    private int _busy;

    public CaptureCoordinator(
        IScreenCapturer capturer,
        Func<MonitorLayout> layoutProvider,
        IRegionSelector selector,
        ICaptureSink sink,
        Func<CaptureSettings> settingsProvider,
        ILogger? logger = null,
        TimeProvider? timeProvider = null)
    {
        _capturer = capturer ?? throw new ArgumentNullException(nameof(capturer));
        _layoutProvider = layoutProvider ?? throw new ArgumentNullException(nameof(layoutProvider));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _logger = logger ?? NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The last confirmed region in virtual-screen pixels, for "repeat last region". Full-screen and monitor captures do not set it.</summary>
    public PixelRect? LastRegion
    {
        get
        {
            lock (_lastRegionLock)
            {
                return _lastRegion;
            }
        }
    }

    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    /// <summary>
    /// Runs one capture. Safe to call from any thread; the capture itself runs on the calling thread until the first
    /// await, so callers on a message loop should hand it to the thread pool.
    /// </summary>
    public async Task<CaptureOutcome> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            Log.CaptureBusy(_logger, request.Mode);
            return CaptureOutcome.Busy;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            Log.CaptureRequested(_logger, request.Mode);
            var settings = _settingsProvider();
            // Clamped here as well as validated in Settings: a value from policy or a hand-edited file is not
            // range-checked on the way in, and an hours-long delay would hold the busy flag the whole time.
            var delay = Math.Clamp(settings.DelayMilliseconds, 0, SettingsValidator.MaxDelayMilliseconds);
            if (delay > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(delay), _timeProvider, cancellationToken).ConfigureAwait(false);
            }

            var layout = _layoutProvider();
            if (request.Mode == CaptureMode.Monitor && (request.MonitorIndex is not { } index || index < 0 || index >= layout.Monitors.Count))
            {
                Log.CaptureNoMonitor(_logger, request.MonitorIndex ?? -1, layout.Monitors.Count);
                return CaptureOutcome.NoMonitor;
            }

            CapturedImage image;
            var frozen = _capturer.Capture(layout, new CaptureOptions { IncludeCursor = settings.CaptureCursor });
            try
            {
                var region = await ResolveRegionAsync(request, frozen, settings, cancellationToken).ConfigureAwait(false);
                if (region is not { } selected)
                {
                    Log.CaptureCancelled(_logger, request.Mode);
                    return CaptureOutcome.Cancelled;
                }

                image = frozen.Crop(layout.ToBitmap(selected));
            }
            finally
            {
                frozen.Dispose();
            }

            if (request.UsesOverlay)
            {
                lock (_lastRegionLock)
                {
                    _lastRegion = image.ScreenRect;
                }
            }

            try
            {
                await _sink.HandleAsync(image, request, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                image.Clear();
                throw;
            }

            Log.CaptureCompleted(_logger, request.Mode, image.Width, image.Height, stopwatch.ElapsedMilliseconds);
            return CaptureOutcome.Completed;
        }
        catch (OperationCanceledException)
        {
            Log.CaptureCancelled(_logger, request.Mode);
            return CaptureOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            Log.CaptureFailed(_logger, request.Mode, ErrorCodes.HResultOf(ex));
            return CaptureOutcome.Failed;
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private async Task<PixelRect?> ResolveRegionAsync(CaptureRequest request, FrozenScreen frozen, CaptureSettings settings, CancellationToken cancellationToken)
    {
        var layout = frozen.Layout;
        switch (request.Mode)
        {
            case CaptureMode.FullScreen:
                return layout.VirtualScreen;

            case CaptureMode.Monitor:
                return layout.Monitors[request.MonitorIndex!.Value].Bounds;

            case CaptureMode.RepeatLastRegion:
                if (LastRegion is { } remembered && Core.Selection.LastRegion.Reconcile(remembered, layout.VirtualScreen) is { } reconciled)
                {
                    return reconciled;
                }

                Log.RepeatRegionFellBack(_logger);
                return await SelectAsync(frozen, OverlayMode.Region, settings, cancellationToken).ConfigureAwait(false);

            case CaptureMode.Window:
                return await SelectAsync(frozen, OverlayMode.Window, settings, cancellationToken).ConfigureAwait(false);

            default:
                return await SelectAsync(frozen, OverlayMode.Region, settings, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<PixelRect?> SelectAsync(FrozenScreen frozen, OverlayMode mode, CaptureSettings settings, CancellationToken cancellationToken)
    {
        var options = new OverlayOptions
        {
            ShowMagnifier = settings.ShowMagnifier,
            ShowCoordinates = settings.ShowCoordinates,
        };
        var result = await _selector.SelectAsync(frozen, mode, options, cancellationToken).ConfigureAwait(false);
        return result.Outcome == OverlayOutcome.Confirmed && !result.Region.IsEmpty ? result.Region : null;
    }
}
