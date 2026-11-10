// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Coordination;

namespace ScrubShot.Capture.Coordination;

/// <summary>
/// Where a finished capture goes: clipboard, file, editor (Milestone 1 step 7 onwards). The sink owns the image from
/// the moment <see cref="HandleAsync"/> is called and must <see cref="CapturedImage.Clear"/> it when it no longer
/// needs the pixels. If the sink throws, the coordinator clears the image itself.
/// </summary>
public interface ICaptureSink
{
    Task HandleAsync(CapturedImage image, CaptureRequest request, CancellationToken cancellationToken);
}

/// <summary>Drops the capture immediately. Used until the output step lands; keeps the pipeline honest about ownership.</summary>
public sealed class DiscardingCaptureSink : ICaptureSink
{
    public static DiscardingCaptureSink Instance { get; } = new();

    public Task HandleAsync(CapturedImage image, CaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        image.Clear();
        return Task.CompletedTask;
    }
}
