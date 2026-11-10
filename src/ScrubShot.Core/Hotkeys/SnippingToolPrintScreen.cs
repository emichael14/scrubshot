// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Security;
using Microsoft.Win32;

namespace ScrubShot.Core.Hotkeys;

/// <summary>
/// The Windows setting that routes the Print Screen key to the Snipping Tool
/// (<c>HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled</c>). It is the most common reason
/// ScrubShot cannot own Print Screen, and it is a per-user value, so the fix needs no elevation (D-013).
/// </summary>
public static class SnippingToolPrintScreen
{
    public const string KeyPath = @"Control Panel\Keyboard";
    public const string ValueName = "PrintScreenKeyForSnippingEnabled";
    public const ushort PrintScreenVirtualKey = 0x2C;

    /// <summary>The first Windows 11 build. From here on the switch defaults to on and the value is written only when the user changes it.</summary>
    public const int FirstWindows11Build = 22000;

    /// <summary>True when Windows currently routes Print Screen to the Snipping Tool.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            return Interpret(key?.GetValue(ValueName), Environment.OSVersion.Version.Build);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the switch the way Windows does: an explicit DWORD wins; when the value is absent, Windows 11
    /// treats the switch as on and Windows 10 as off.
    /// </summary>
    public static bool Interpret(object? registryValue, int osBuild) =>
        registryValue is int value ? value != 0 : osBuild >= FirstWindows11Build;

    /// <summary>Turns the routing off so Print Screen is free. Only call this after the user asked for it.</summary>
    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            key.SetValue(ValueName, 0, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>True when <paramref name="gesture"/> is the bare Print Screen key, the only combination the Snipping Tool takes.</summary>
    public static bool Claims(HotkeyGesture gesture) =>
        gesture.VirtualKey == PrintScreenVirtualKey && !gesture.HasModifiers;
}
