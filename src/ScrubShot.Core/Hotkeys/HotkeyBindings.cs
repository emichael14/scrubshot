// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Settings;

namespace ScrubShot.Core.Hotkeys;

/// <summary>The capture actions a hotkey can trigger. The numeric values are the Win32 hotkey ids.</summary>
public enum HotkeyAction
{
    Region = 1,
    Window = 2,
    FullScreen = 3,
    RepeatLastRegion = 4,
}

/// <summary>Why a hotkey could not be registered.</summary>
public enum HotkeyConflict
{
    None,

    /// <summary>The settings string could not be parsed; the action has no hotkey until it is fixed.</summary>
    InvalidGesture,

    /// <summary>Another application already owns the combination (Win32 reported it as already registered).</summary>
    OwnedByAnotherApp,

    /// <summary>Print Screen is routed to the Windows Snipping Tool (the registry switch is on); ScrubShot can fix this with one click.</summary>
    OwnedBySnippingTool,

    /// <summary>The key is reserved by Windows (F12 belongs to the debugger) and is never registered.</summary>
    ReservedKey,

    /// <summary>An ordinary key with no modifier would be taken away from every application; only Print Screen, Pause, Scroll Lock and the F-keys may stand alone.</summary>
    NeedsModifier,

    /// <summary>The same gesture is assigned to an earlier action in these settings; the first action keeps it.</summary>
    DuplicateInSettings,

    /// <summary>Registration failed for another reason (error code in <see cref="HotkeyBinding.ErrorCode"/>).</summary>
    Failed,
}

/// <summary>One action's hotkey as requested by settings and as the system accepted it.</summary>
public sealed record HotkeyBinding(HotkeyAction Action, string GestureText, HotkeyGesture? Gesture, HotkeyConflict Conflict, int ErrorCode = 0)
{
    public bool IsRegistered => Conflict == HotkeyConflict.None && Gesture is not null;
}

/// <summary>Maps the settings strings to parsed gestures per action, without touching Win32.</summary>
public static class HotkeyBindings
{
    public static IReadOnlyList<(HotkeyAction Action, string Text)> FromSettings(HotkeySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return
        [
            (HotkeyAction.Region, settings.Region),
            (HotkeyAction.Window, settings.Window),
            (HotkeyAction.FullScreen, settings.FullScreen),
            (HotkeyAction.RepeatLastRegion, settings.RepeatLastRegion),
        ];
    }

    /// <summary>Finds actions whose gestures collide with each other in the settings themselves.</summary>
    public static IReadOnlyList<(HotkeyAction First, HotkeyAction Second)> Duplicates(HotkeySettings settings)
    {
        var parsed = FromSettings(settings)
            .Select(b => (b.Action, Gesture: HotkeyGesture.TryParse(b.Text, out var g) ? g : null))
            .Where(b => b.Gesture is not null)
            .ToList();

        var duplicates = new List<(HotkeyAction, HotkeyAction)>();
        for (var i = 0; i < parsed.Count; i++)
        {
            for (var j = i + 1; j < parsed.Count; j++)
            {
                if (parsed[i].Gesture == parsed[j].Gesture)
                {
                    duplicates.Add((parsed[i].Action, parsed[j].Action));
                }
            }
        }

        return duplicates;
    }
}
