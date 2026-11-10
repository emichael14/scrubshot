// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using Microsoft.Extensions.Logging;

namespace ScrubShot.Core.Logging;

/// <summary>Which output step failed. Logged instead of any detail about the content or destination.</summary>
public enum OutputStage
{
    Clipboard,
    File,

    /// <summary>A subscriber to the output result (the notification) threw.</summary>
    Notification,
}

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
        Message = "The hotkey service could not start (HRESULT {HResult}).")]
    public static partial void HotkeyServiceFailed(ILogger logger, int hResult);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Error,
        Message = "A hotkey handler threw (HRESULT {HResult}).")]
    public static partial void HotkeyHandlerFailed(ILogger logger, int hResult);

    // 13xx: capture coordination. Modes and outcomes are enums; sizes and timings are numbers. Never pixels or regions.

    [LoggerMessage(EventId = 1300, Level = LogLevel.Debug,
        Message = "Capture requested: {Mode}.")]
    public static partial void CaptureRequested(ILogger logger, Core.Coordination.CaptureMode mode);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Information,
        Message = "Capture request {Mode} dropped: another capture is in progress.")]
    public static partial void CaptureBusy(ILogger logger, Core.Coordination.CaptureMode mode);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Debug,
        Message = "Capture {Mode} cancelled.")]
    public static partial void CaptureCancelled(ILogger logger, Core.Coordination.CaptureMode mode);

    [LoggerMessage(EventId = 1303, Level = LogLevel.Information,
        Message = "Capture {Mode} completed: {Width}x{Height} in {ElapsedMs} ms.")]
    public static partial void CaptureCompleted(ILogger logger, Core.Coordination.CaptureMode mode, int width, int height, long elapsedMs);

    [LoggerMessage(EventId = 1304, Level = LogLevel.Error,
        Message = "Capture {Mode} failed (HRESULT {HResult}).")]
    public static partial void CaptureFailed(ILogger logger, Core.Coordination.CaptureMode mode, int hResult);

    [LoggerMessage(EventId = 1305, Level = LogLevel.Warning,
        Message = "Capture of monitor {Index} skipped: the layout has {Count} monitor(s).")]
    public static partial void CaptureNoMonitor(ILogger logger, int index, int count);

    [LoggerMessage(EventId = 1306, Level = LogLevel.Information,
        Message = "Repeat last region fell back to region select: no remembered region fits the current layout.")]
    public static partial void RepeatRegionFellBack(ILogger logger);

    // 15xx: output. Formats, flags, byte counts and timings. Never a path or a pixel.

    [LoggerMessage(EventId = 1500, Level = LogLevel.Information,
        Message = "Capture copied to the clipboard (excluded from history: {Excluded}) in {ElapsedMs} ms.")]
    public static partial void OutputCopied(ILogger logger, bool excluded, long elapsedMs);

    [LoggerMessage(EventId = 1501, Level = LogLevel.Information,
        Message = "Capture saved as {Format}, {Bytes} bytes, in {ElapsedMs} ms.")]
    public static partial void OutputSaved(ILogger logger, Core.Settings.ImageFormat format, long bytes, long elapsedMs);

    [LoggerMessage(EventId = 1502, Level = LogLevel.Error,
        Message = "Output stage {Stage} failed (HRESULT {HResult}).")]
    public static partial void OutputFailed(ILogger logger, OutputStage stage, int hResult);

    [LoggerMessage(EventId = 1503, Level = LogLevel.Warning,
        Message = "Open last capture: the saved file is gone; the in-memory copy was re-copied instead: {Recopied}.")]
    public static partial void LastCaptureFileMissing(ILogger logger, bool recopied);

    // 14xx: process lifecycle.

    [LoggerMessage(EventId = 1400, Level = LogLevel.Information,
        Message = "Another ScrubShot instance is running; it was signalled and this one exits.")]
    public static partial void SecondInstanceExiting(ILogger logger);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Information,
        Message = "A second launch asked this instance to come forward.")]
    public static partial void ActivationRequested(ILogger logger);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Information,
        Message = "Launch-at-startup registration synced: {Result}.")]
    public static partial void StartupRegistrationSynced(ILogger logger, Core.Lifecycle.StartupSyncResult result);

    [LoggerMessage(EventId = 1403, Level = LogLevel.Warning,
        Message = "Launch-at-startup registration not applied: {Result} (HRESULT {HResult}).")]
    public static partial void StartupRegistrationFailed(ILogger logger, Core.Lifecycle.StartupSyncResult result, int hResult);

    [LoggerMessage(EventId = 1404, Level = LogLevel.Warning,
        Message = "The tray icon could not be added (attempt {Attempt}, HRESULT {HResult}).")]
    public static partial void TrayIconFailed(ILogger logger, int attempt, int hResult);
}
