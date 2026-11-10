// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Diagnostics.CodeAnalysis;

namespace ScrubShot.Core.Hotkeys;

/// <summary>Modifier keys of a hotkey. Values match the Win32 <c>MOD_*</c> flags so no translation is needed.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
}

/// <summary>
/// A global hotkey as stored in settings: modifiers plus one named key, written like <c>Ctrl+Shift+PrintScreen</c>.
/// Parsing is case-insensitive and accepts the common spellings (Ctrl or Control, Win or Windows, PrtSc or PrintScreen).
/// The key is kept as a Win32 virtual-key code so the capture layer can register it directly.
/// </summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, ushort VirtualKey)
{
    private static readonly Dictionary<string, ushort> KeysByName = BuildKeyTable();
    private static readonly Dictionary<ushort, string> NamesByKey = KeysByName
        .GroupBy(p => p.Value)
        .ToDictionary(g => g.Key, g => g.First().Key);

    /// <summary>Every key name a user can type, in display form.</summary>
    public static IReadOnlyCollection<string> KeyNames => NamesByKey.Values.Order(StringComparer.Ordinal).ToList();

    public static bool TryParse(string? text, [NotNullWhen(true)] out HotkeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        ushort key = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0)
            {
                return false; // "Ctrl++A" or a trailing plus is a typo, not a gesture
            }

            switch (raw.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "ALT":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "SHIFT":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "WIN" or "WINDOWS" or "META" or "SUPER":
                    modifiers |= HotkeyModifiers.Windows;
                    break;
                default:
                    if (key != 0 || !KeysByName.TryGetValue(raw, out key))
                    {
                        return false;
                    }

                    break;
            }
        }

        if (key == 0)
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    public static HotkeyGesture Parse(string text) =>
        TryParse(text, out var gesture) ? gesture : throw new FormatException($"'{text}' is not a valid hotkey.");

    public bool HasModifiers => Modifiers != HotkeyModifiers.None;

    /// <summary>
    /// True when the gesture may be registered as-is: it has a modifier, or its key is one that is harmless to
    /// own system-wide on its own (Print Screen, Pause, Scroll Lock, F1 to F24). A bare letter, digit, Enter or
    /// Space would be stolen from every application.
    /// </summary>
    public bool IsRegistrable => HasModifiers || IsStandaloneKey(VirtualKey);

    public static bool IsStandaloneKey(ushort virtualKey) =>
        virtualKey is 0x2C or 0x13 or 0x91 || virtualKey is >= 0x70 and <= 0x87;

    public string KeyName => NamesByKey.TryGetValue(VirtualKey, out var name) ? name : $"0x{VirtualKey:X2}";

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(KeyName);
        return string.Join('+', parts);
    }

    private static Dictionary<string, ushort> BuildKeyTable()
    {
        var table = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["PrintScreen"] = 0x2C,
            ["PrtSc"] = 0x2C,
            ["PrtScn"] = 0x2C,
            ["Snapshot"] = 0x2C,
            ["Pause"] = 0x13,
            ["ScrollLock"] = 0x91,
            ["Insert"] = 0x2D,
            ["Delete"] = 0x2E,
            ["Home"] = 0x24,
            ["End"] = 0x23,
            ["PageUp"] = 0x21,
            ["PageDown"] = 0x22,
            ["Space"] = 0x20,
            ["Tab"] = 0x09,
            ["Backspace"] = 0x08,
            ["Enter"] = 0x0D,
            ["Up"] = 0x26,
            ["Down"] = 0x28,
            ["Left"] = 0x25,
            ["Right"] = 0x27,
            ["NumPad0"] = 0x60,
            ["NumPad1"] = 0x61,
            ["NumPad2"] = 0x62,
            ["NumPad3"] = 0x63,
            ["NumPad4"] = 0x64,
            ["NumPad5"] = 0x65,
            ["NumPad6"] = 0x66,
            ["NumPad7"] = 0x67,
            ["NumPad8"] = 0x68,
            ["NumPad9"] = 0x69,
            ["Multiply"] = 0x6A,
            ["Add"] = 0x6B,
            ["Subtract"] = 0x6D,
            ["Decimal"] = 0x6E,
            ["Divide"] = 0x6F,
        };

        for (var i = 1; i <= 24; i++)
        {
            table[$"F{i}"] = (ushort)(0x70 + i - 1);
        }

        for (var c = 'A'; c <= 'Z'; c++)
        {
            table[c.ToString()] = (ushort)c;
        }

        for (var d = '0'; d <= '9'; d++)
        {
            table[d.ToString()] = (ushort)d;
        }

        return table;
    }
}
