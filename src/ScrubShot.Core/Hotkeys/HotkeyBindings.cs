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

    /// <summary>Another action in these settings already holds the same gesture: the one set by policy if there is one, otherwise the earlier action.</summary>
    DuplicateInSettings,

    /// <summary>Registration failed for another reason (error code in <see cref="HotkeyBinding.ErrorCode"/>).</summary>
    Failed,
}

/// <summary>One action's hotkey as requested by settings and as the system accepted it.</summary>
public sealed record HotkeyBinding(HotkeyAction Action, string GestureText, HotkeyGesture? Gesture, HotkeyConflict Conflict, int ErrorCode = 0)
{
    public bool IsRegistered => Conflict == HotkeyConflict.None && Gesture is not null;
}

/// <summary>One action's hotkey after the checks that need no Win32: parsing, the reserved key, the modifier rule, duplicates.</summary>
public sealed record HotkeyPrecheck(HotkeyAction Action, string GestureText, HotkeyGesture? Gesture, HotkeyConflict Conflict)
{
    /// <summary>True when the gesture may be handed to RegisterHotKey.</summary>
    public bool CanRegister => Conflict == HotkeyConflict.None && Gesture is not null;
}

/// <summary>Maps the settings strings to parsed gestures per action, without touching Win32.</summary>
public static class HotkeyBindings
{
    /// <summary>F12 is reserved for the debugger at all times (Learn: RegisterHotKey remarks) and is never registered.</summary>
    public const ushort ReservedDebuggerKey = 0x7B;

    /// <summary>
    /// The rules shared by the hotkey service and the Settings window: the text must parse, F12 is reserved, an
    /// ordinary key needs a modifier, and a gesture can belong to one action only. When two actions ask for the
    /// same gesture, a hotkey set by policy wins over one that is not (machine policy before user policy), so a
    /// default or a user's own choice can never take a gesture away from one the organization fixed; otherwise
    /// the earlier action keeps it. Results are in action order whatever the claim order was.
    /// </summary>
    /// <param name="settings">The four gesture strings.</param>
    /// <param name="sources">Where each action's value came from. Null means no policy is involved.</param>
    public static IReadOnlyList<HotkeyPrecheck> Precheck(HotkeySettings settings, IReadOnlyDictionary<HotkeyAction, SettingSource>? sources = null)
    {
        var entries = FromSettings(settings);
        var results = new HotkeyPrecheck[entries.Count];
        var taken = new HashSet<HotkeyGesture>();
        var claimOrder = Enumerable.Range(0, entries.Count)
            .OrderBy(index => Rank(sources, entries[index].Action))
            .ThenBy(index => index);

        foreach (var index in claimOrder)
        {
            var (action, text) = entries[index];
            if (!HotkeyGesture.TryParse(text, out var gesture))
            {
                results[index] = new HotkeyPrecheck(action, text, null, HotkeyConflict.InvalidGesture);
            }
            else if (gesture.VirtualKey == ReservedDebuggerKey)
            {
                results[index] = new HotkeyPrecheck(action, text, gesture, HotkeyConflict.ReservedKey);
            }
            else if (!gesture.IsRegistrable)
            {
                results[index] = new HotkeyPrecheck(action, text, gesture, HotkeyConflict.NeedsModifier);
            }
            else if (!taken.Add(gesture))
            {
                results[index] = new HotkeyPrecheck(action, text, gesture, HotkeyConflict.DuplicateInSettings);
            }
            else
            {
                results[index] = new HotkeyPrecheck(action, text, gesture, HotkeyConflict.None);
            }
        }

        return results;
    }

    private static int Rank(IReadOnlyDictionary<HotkeyAction, SettingSource>? sources, HotkeyAction action) =>
        sources is not null && sources.TryGetValue(action, out var source)
            ? source switch
            {
                SettingSource.MachinePolicy => 0,
                SettingSource.UserPolicy => 1,
                _ => 2,
            }
            : 2;

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
