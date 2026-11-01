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

    // 11xx: settings and policy. Error codes are HRESULTs; never paths or values.

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information,
        Message = "Settings loaded. Schema version {SchemaVersion}. Defaults used: {UsedDefaults}.")]
    public static partial void SettingsLoaded(ILogger logger, int schemaVersion, bool usedDefaults);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning,
        Message = "Settings file could not be read (HRESULT {HResult}). Defaults are in effect.")]
    public static partial void SettingsLoadFailed(ILogger logger, int hResult);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Error,
        Message = "Settings file could not be written (HRESULT {HResult}).")]
    public static partial void SettingsSaveFailed(ILogger logger, int hResult);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Warning,
        Message = "Policy registry key could not be read (HRESULT {HResult}). Policy from that hive is not applied.")]
    public static partial void PolicyReadFailed(ILogger logger, int hResult);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Warning,
        Message = "{Count} policy value(s) ignored: unknown setting name or value not convertible to the setting type.")]
    public static partial void PolicyValuesIgnored(ILogger logger, int count);

    [LoggerMessage(EventId = 1105, Level = LogLevel.Warning,
        Message = "{Count} setting(s) in the settings document were missing, null or of the wrong type and were reset to defaults.")]
    public static partial void SettingsValuesReset(ILogger logger, int count);

    // 12xx: hotkeys. Actions and conflicts are enums; never the gesture text.

    [LoggerMessage(EventId = 1200, Level = LogLevel.Information,
        Message = "Hotkeys applied. Registered {Registered}, conflicts {Conflicts}.")]
    public static partial void HotkeysApplied(ILogger logger, int registered, int conflicts);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Warning,
        Message = "Hotkey for {Action} not registered: {Conflict} (error {ErrorCode}).")]
    public static partial void HotkeyConflict(ILogger logger, Core.Hotkeys.HotkeyAction action, Core.Hotkeys.HotkeyConflict conflict, int errorCode);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Debug,
        Message = "Hotkey pressed: {Action}.")]
    public static partial void HotkeyPressed(ILogger logger, Core.Hotkeys.HotkeyAction action);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Error,
        Message = "The hotkey service could not start (error {ErrorCode}).")]
    public static partial void HotkeyServiceFailed(ILogger logger, int errorCode);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Error,
        Message = "A hotkey handler threw (HRESULT {HResult}).")]
    public static partial void HotkeyHandlerFailed(ILogger logger, int hResult);
}
