// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScrubShot.Capture;
using ScrubShot.Capture.Coordination;
using ScrubShot.Core.Coordination;
using ScrubShot.Core.Logging;
using ScrubShot.Core.Settings;

namespace ScrubShot.Output;

/// <summary>
/// What happened to a capture, per output. Shown to the user; never logged with the path. A capture can succeed in
/// one output and fail in the other (copied, but the save folder was not writable).
/// </summary>
public sealed record OutputResult(bool Copied, string? SavedPath, bool ExcludedFromHistory, bool CopyFailed = false, bool SaveFailed = false)
{
    public bool AnySucceeded => Copied || SavedPath is not null;

    public bool AnyFailed => CopyFailed || SaveFailed;
}

/// <summary>
/// The capture sink for Milestone 1: applies the default action from settings (Copy, Save, or both) and remembers
/// the last capture that reached an output so "Open last capture" can reopen the file or re-copy the in-memory
/// image (D-043). The two outputs are independent: one failing does not stop the other, and every outcome, failures
/// included, is reported through <see cref="Completed"/>. The sink owns the image it receives; the previous one is
/// cleared when a new capture replaces it, when nothing succeeded, and on dispose.
/// </summary>
public sealed class OutputSink : ICaptureSink, IDisposable
{
    private readonly IClipboardWriter _clipboard;
    private readonly ICaptureSaver _saver;
    private readonly Func<OutputSettings> _settingsProvider;
    private readonly ILogger _logger;
    private readonly Lock _lastLock = new();

    // Held while the last image's pixels are being read for a re-copy, and while any remembered image is zeroed,
    // so a re-copy can never encode a buffer that is being cleared under it. Never disposed: a re-copy racing
    // the app's exit may still release it.
    private readonly SemaphoreSlim _imageGate = new(1, 1);

    private CapturedImage? _lastImage;
    private OutputResult? _lastResult;
    private bool _disposed;

    public OutputSink(IClipboardWriter clipboard, ICaptureSaver saver, Func<OutputSettings> settingsProvider, ILogger? logger = null)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _saver = saver ?? throw new ArgumentNullException(nameof(saver));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Raised on the calling thread after each capture has been through its outputs, whether they succeeded or not.
    /// Marshal to the UI thread to show a notification. An exception from a handler is logged and dropped.
    /// </summary>
    public event Action<OutputResult>? Completed;

    /// <summary>The outcome of the last capture that reached at least one output, or null before the first.</summary>
    public OutputResult? LastResult
    {
        get
        {
            lock (_lastLock)
            {
                return _lastResult;
            }
        }
    }

    public bool HasLastCapture => LastResult is not null;

    public async Task HandleAsync(CapturedImage image, CaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        var settings = _settingsProvider();
        var copy = settings.DefaultAction is DefaultCaptureAction.Copy or DefaultCaptureAction.CopyAndSave;
        var save = settings.DefaultAction is DefaultCaptureAction.Save or DefaultCaptureAction.CopyAndSave;

        var copied = false;
        var excluded = false;
        var copyFailed = false;
        var saveFailed = false;
        string? savedPath = null;
        ExceptionDispatchInfo? firstFailure = null;
        try
        {
            if (copy)
            {
                try
                {
                    excluded = await CopyAsync(image, settings, cancellationToken).ConfigureAwait(false);
                    copied = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    copyFailed = true;
                    firstFailure = ExceptionDispatchInfo.Capture(ex);
                }
            }

            if (save)
            {
                try
                {
                    savedPath = await SaveAsync(image, settings, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    saveFailed = true;
                    firstFailure ??= ExceptionDispatchInfo.Capture(ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            image.Clear();
            throw;
        }

        var result = new OutputResult(copied, savedPath, excluded, copyFailed, saveFailed);
        if (result.AnySucceeded)
        {
            // The capture exists somewhere the user can reach, so it becomes "the last capture" even if the other
            // output failed; the notification says which part did not work.
            Remember(image, result);
            RaiseCompleted(result);
            return;
        }

        // Nothing reached an output. The previous capture stays the last one; this one is gone.
        image.Clear();
        RaiseCompleted(result);
        firstFailure?.Throw();
    }

    /// <summary>
    /// Re-copies the last capture to the clipboard. Returns false when there is none. The app calls this for
    /// "Open last capture" when nothing was saved.
    /// </summary>
    public async Task<bool> RecopyLastAsync(CancellationToken cancellationToken = default)
    {
        await _imageGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CapturedImage? image;
            lock (_lastLock)
            {
                image = _lastImage;
            }

            if (image is null)
            {
                return false;
            }

            await CopyAsync(image, _settingsProvider(), cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _imageGate.Release();
        }
    }

    public void Dispose()
    {
        _imageGate.Wait();
        try
        {
            lock (_lastLock)
            {
                _disposed = true;
                _lastImage?.Clear();
                _lastImage = null;
                _lastResult = null;
            }
        }
        finally
        {
            _imageGate.Release();
        }
    }

    private async Task<bool> CopyAsync(CapturedImage image, OutputSettings settings, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        byte[]? png = null;
        byte[]? dib = null;
        try
        {
            png = await ImageEncoder.EncodePngAsync(image, cancellationToken).ConfigureAwait(false);
            dib = DibBuilder.Build(image);
            _clipboard.SetImage(png, dib, settings.ExcludeFromClipboardHistory);
            Log.OutputCopied(_logger, settings.ExcludeFromClipboardHistory, stopwatch.ElapsedMilliseconds);
            return settings.ExcludeFromClipboardHistory;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.OutputFailed(_logger, OutputStage.Clipboard, ErrorCodes.HResultOf(ex));
            throw;
        }
        finally
        {
            // The clipboard holds its own copy. Ours are zeroed rather than left for the collector: best effort,
            // like CapturedImage.Clear, against the pixels lingering in the managed heap.
            Zero(png);
            Zero(dib);
        }
    }

    private async Task<string> SaveAsync(CapturedImage image, OutputSettings settings, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        byte[]? encoded = null;
        try
        {
            encoded = await ImageEncoder.EncodeAsync(image, settings.Format, settings.JpgQuality, cancellationToken).ConfigureAwait(false);
            var path = await _saver.SaveAsync(encoded, settings, image.CapturedAt, cancellationToken).ConfigureAwait(false);
            Log.OutputSaved(_logger, settings.Format, encoded.LongLength, stopwatch.ElapsedMilliseconds);
            return path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.OutputFailed(_logger, OutputStage.File, ErrorCodes.HResultOf(ex));
            throw;
        }
        finally
        {
            Zero(encoded);
        }
    }

    private static void Zero(byte[]? buffer)
    {
        if (buffer is not null)
        {
            Array.Clear(buffer);
        }
    }

    private void RaiseCompleted(OutputResult result)
    {
        try
        {
            Completed?.Invoke(result);
        }
        catch (Exception ex)
        {
            // A subscriber's failure must not turn a finished capture into a failed one, nor reach the
            // coordinator, which would clear the image this sink has just remembered.
            Log.OutputFailed(_logger, OutputStage.Notification, ErrorCodes.HResultOf(ex));
        }
    }

    private void Remember(CapturedImage image, OutputResult result)
    {
        CapturedImage? previous = null;
        bool keep;
        lock (_lastLock)
        {
            keep = !_disposed;
            if (keep)
            {
                previous = _lastImage;
                _lastImage = image;
                _lastResult = result;
            }
        }

        if (!keep)
        {
            image.Clear(); // the app is exiting; nothing will ever ask for this capture again
            return;
        }

        if (previous is not null && !ReferenceEquals(previous, image))
        {
            _imageGate.Wait();
            try
            {
                previous.Clear();
            }
            finally
            {
                _imageGate.Release();
            }
        }
    }
}
