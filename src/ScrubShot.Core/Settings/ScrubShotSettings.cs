// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

namespace ScrubShot.Core.Settings;

/// <summary>What happens to a capture when the post-capture menu is skipped or does not exist yet.</summary>
public enum DefaultCaptureAction
{
    Copy,
    Save,
    CopyAndSave,
}

/// <summary>File format for saved captures.</summary>
public enum ImageFormat
{
    Png,
    Jpg,
}

/// <summary>Application theme.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

public sealed record GeneralSettings
{
    public bool LaunchAtStartup { get; init; } = true;

    public AppTheme Theme { get; init; } = AppTheme.System;
}

public sealed record OutputSettings
{
    public DefaultCaptureAction DefaultAction { get; init; } = DefaultCaptureAction.Copy;

    /// <summary>May contain environment variables; expand with <see cref="Environment.ExpandEnvironmentVariables"/> at use time.</summary>
    public string SaveFolder { get; init; } = @"%USERPROFILE%\Pictures\ScrubShot";

    /// <summary>Tokens are documented on <see cref="Output.FileNamePattern"/>.</summary>
    public string FileNamePattern { get; init; } = Output.FileNamePattern.DefaultPattern;

    public ImageFormat Format { get; init; } = ImageFormat.Png;

    /// <summary>JPEG quality from 1 to 100. Ignored for PNG.</summary>
    public int JpgQuality { get; init; } = 90;

    /// <summary>Exclude ScrubShot clipboard writes from Windows clipboard history and cloud sync (D-012).</summary>
    public bool ExcludeFromClipboardHistory { get; init; } = true;
}

public sealed record CaptureSettings
{
    public bool CaptureCursor { get; init; }

    public int DelayMilliseconds { get; init; }

    public bool ShowMagnifier { get; init; } = true;

    public bool ShowCoordinates { get; init; } = true;
}

/// <summary>
/// Hotkeys as gesture strings such as "Ctrl+Shift+PrintScreen". Parsing and registration arrive with the
/// hotkey service (Milestone 1 step 5); the settings layer stores and resolves them as text.
/// </summary>
public sealed record HotkeySettings
{
    public string Region { get; init; } = "PrintScreen";

    public string Window { get; init; } = "Alt+PrintScreen";

    public string FullScreen { get; init; } = "Ctrl+PrintScreen";

    public string RepeatLastRegion { get; init; } = "Shift+PrintScreen";
}

public sealed record AdvancedSettings
{
    /// <summary>Opt-in local debug log. Content-free by construction (D-014).</summary>
    public bool DebugLogging { get; init; }
}

/// <summary>
/// The complete user settings document. Every leaf property is a setting whose path is
/// "Section.Name" (for example "Output.DefaultAction"); that path is also the registry value
/// name used by admin policy. Sections and names are added only in the milestone that uses them;
/// reserved for Milestone 3: "Scrubbing.AiLayerEnabled" and "Scrubbing.SensitivityLevel".
/// </summary>
public sealed record ScrubShotSettings
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>Version of this document's shape. Not a setting; policy cannot set it.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public GeneralSettings General { get; init; } = new();

    public OutputSettings Output { get; init; } = new();

    public CaptureSettings Capture { get; init; } = new();

    public HotkeySettings Hotkeys { get; init; } = new();

    public AdvancedSettings Advanced { get; init; } = new();

    public static ScrubShotSettings Defaults => new();
}
