// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Hotkeys;

namespace ScrubShot.Core.Coordination;

/// <summary>What the user asked to capture. The hotkey actions map onto the first four; the tray menu adds monitors.</summary>
public enum CaptureMode
{
    /// <summary>Freeze the screen and let the user drag a rectangle.</summary>
    Region,

    /// <summary>Freeze the screen and let the user pick a window.</summary>
    Window,

    /// <summary>The whole virtual screen, no overlay.</summary>
    FullScreen,

    /// <summary>One monitor by index, no overlay.</summary>
    Monitor,

    /// <summary>The last confirmed region, reconciled with the current layout; falls back to region select when none fits.</summary>
    RepeatLastRegion,
}

/// <summary>One capture request. <see cref="MonitorIndex"/> is only meaningful for <see cref="CaptureMode.Monitor"/>.</summary>
public sealed record CaptureRequest(CaptureMode Mode, int? MonitorIndex = null)
{
    public static CaptureRequest Region { get; } = new(CaptureMode.Region);

    public static CaptureRequest Window { get; } = new(CaptureMode.Window);

    public static CaptureRequest FullScreen { get; } = new(CaptureMode.FullScreen);

    public static CaptureRequest RepeatLastRegion { get; } = new(CaptureMode.RepeatLastRegion);

    public static CaptureRequest ForMonitor(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new(CaptureMode.Monitor, index);
    }

    public static CaptureRequest FromHotkey(HotkeyAction action) => action switch
    {
        HotkeyAction.Region => Region,
        HotkeyAction.Window => Window,
        HotkeyAction.FullScreen => FullScreen,
        HotkeyAction.RepeatLastRegion => RepeatLastRegion,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown hotkey action."),
    };

    /// <summary>True when this mode shows the selection overlay (or may, for repeat without a usable last region).</summary>
    public bool UsesOverlay => Mode is CaptureMode.Region or CaptureMode.Window or CaptureMode.RepeatLastRegion;
}

/// <summary>How a capture request ended. Every outcome except <see cref="Completed"/> means nothing reached the output.</summary>
public enum CaptureOutcome
{
    /// <summary>The image was handed to the output sink.</summary>
    Completed,

    /// <summary>The user cancelled the overlay, or the request was cancelled.</summary>
    Cancelled,

    /// <summary>Another capture was already in progress; this request was dropped.</summary>
    Busy,

    /// <summary>The requested monitor index does not exist in the current layout.</summary>
    NoMonitor,

    /// <summary>Capture, selection or output threw. Details are in the log as an error code only.</summary>
    Failed,
}
