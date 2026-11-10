// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Hotkeys;
using ScrubShot.Core.Settings;

namespace ScrubShot.Tests.Core;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("PrintScreen", HotkeyModifiers.None, 0x2C)]
    [InlineData("Alt+PrintScreen", HotkeyModifiers.Alt, 0x2C)]
    [InlineData("Ctrl+PrintScreen", HotkeyModifiers.Control, 0x2C)]
    [InlineData("Shift+PrintScreen", HotkeyModifiers.Shift, 0x2C)]
    [InlineData("ctrl + shift + prtsc", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x2C)]
    [InlineData("Win+S", HotkeyModifiers.Windows, 0x53)]
    [InlineData("Control+Alt+F12", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x7B)]
    [InlineData("F1", HotkeyModifiers.None, 0x70)]
    [InlineData("Ctrl+NumPad5", HotkeyModifiers.Control, 0x65)]
    [InlineData("Alt+0", HotkeyModifiers.Alt, 0x30)]
    public void Parses_common_spellings(string text, HotkeyModifiers modifiers, int key)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal((ushort)key, gesture.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Banana")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl++A")]
    [InlineData("F25")]
    public void Rejects_invalid_text(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Null(gesture);
        Assert.Throws<FormatException>(() => HotkeyGesture.Parse(text));
    }

    [Fact]
    public void Null_is_rejected()
    {
        Assert.False(HotkeyGesture.TryParse(null, out _));
    }

    [Theory]
    [InlineData("PrintScreen", "PrintScreen")]
    [InlineData("shift+ctrl+alt+win+a", "Ctrl+Alt+Shift+Win+A")]
    [InlineData("prtsc", "PrintScreen")]
    [InlineData("ALT+f5", "Alt+F5")]
    public void Formats_canonically_and_round_trips(string text, string expected)
    {
        var gesture = HotkeyGesture.Parse(text);

        Assert.Equal(expected, gesture.ToString());
        Assert.Equal(gesture, HotkeyGesture.Parse(gesture.ToString()));
    }

    [Fact]
    public void Modifier_values_match_the_win32_flags()
    {
        Assert.Equal(1, (int)HotkeyModifiers.Alt);
        Assert.Equal(2, (int)HotkeyModifiers.Control);
        Assert.Equal(4, (int)HotkeyModifiers.Shift);
        Assert.Equal(8, (int)HotkeyModifiers.Windows);
    }

    [Fact]
    public void Default_settings_parse_and_do_not_collide()
    {
        var settings = new HotkeySettings();

        foreach (var (action, text) in HotkeyBindings.FromSettings(settings))
        {
            Assert.True(HotkeyGesture.TryParse(text, out _), $"{action}: {text}");
        }

        Assert.Empty(HotkeyBindings.Duplicates(settings));
    }

    [Fact]
    public void Duplicates_in_settings_are_reported()
    {
        var settings = new HotkeySettings { Region = "F9", Window = "f9", FullScreen = "Ctrl+F9" };

        var duplicates = HotkeyBindings.Duplicates(settings);

        Assert.Equal([(HotkeyAction.Region, HotkeyAction.Window)], duplicates);
    }

    [Theory]
    [InlineData("PrintScreen", true)]
    [InlineData("F5", true)]
    [InlineData("Pause", true)]
    [InlineData("ScrollLock", true)]
    [InlineData("Ctrl+A", true)]
    [InlineData("Win+Space", true)]
    [InlineData("A", false)]
    [InlineData("Enter", false)]
    [InlineData("Space", false)]
    [InlineData("5", false)]
    [InlineData("NumPad5", false)]
    public void Only_modified_or_standalone_safe_keys_are_registrable(string text, bool registrable)
    {
        Assert.Equal(registrable, HotkeyGesture.Parse(text).IsRegistrable);
    }

    [Fact]
    public void Snipping_tool_claims_only_the_bare_print_screen_key()
    {
        Assert.True(SnippingToolPrintScreen.Claims(HotkeyGesture.Parse("PrintScreen")));
        Assert.False(SnippingToolPrintScreen.Claims(HotkeyGesture.Parse("Alt+PrintScreen")));
        Assert.False(SnippingToolPrintScreen.Claims(HotkeyGesture.Parse("F12")));
    }

    [Theory]
    [InlineData(1, 19045, true)]
    [InlineData(0, 19045, false)]
    [InlineData(1, 26100, true)]
    [InlineData(0, 26100, false)]
    [InlineData(null, 19045, false)]
    [InlineData(null, 22000, true)]
    [InlineData(null, 26100, true)]
    [InlineData("1", 26100, true)]
    public void Snipping_tool_switch_defaults_to_on_only_on_windows_11(object? value, int build, bool expected)
    {
        Assert.Equal(expected, SnippingToolPrintScreen.Interpret(value, build));
    }

    [Fact]
    public void Key_names_include_the_documented_ones()
    {
        var names = HotkeyGesture.KeyNames;

        Assert.Contains("PrintScreen", names);
        Assert.Contains("F12", names);
        Assert.Contains("A", names);
        Assert.DoesNotContain("PrtSc", names);
    }
}
