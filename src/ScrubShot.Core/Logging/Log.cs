// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using Microsoft.Extensions.Logging;

namespace ScrubShot.Core.Logging;

/// <summary>
/// Every log call site in ScrubShot goes through this class.
/// </summary>
/// <remarks>
/// Parameters are restricted to event-shaped data: versions, counts, durations, flags, enums and
/// error codes. There is deliberately no method that accepts free text, so captured content,
/// capture file names and detected values cannot reach a log. A unit test enforces the parameter
/// types. See docs/decisions.md D-014.
/// </remarks>
public static partial class Log
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information,
        Message = "Application started. Version {Version}. PerMonitorV2 DPI awareness: {IsPerMonitorV2}.")]
    public static partial void AppStarted(ILogger logger, Version version, bool isPerMonitorV2);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Application exiting. Exit code {ExitCode}.")]
    public static partial void AppExiting(ILogger logger, int exitCode);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "Process is not PerMonitorV2 DPI aware. Capture coordinates may be scaled by the system.")]
    public static partial void DpiAwarenessUnexpected(ILogger logger);
}
