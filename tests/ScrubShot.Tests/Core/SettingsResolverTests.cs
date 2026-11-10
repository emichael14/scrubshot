// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Settings;

namespace ScrubShot.Tests.Core;

public class SettingsResolverTests
{
    private static InMemoryPolicySource Policy(params (string Name, object Value)[] values) =>
        new(values.ToDictionary(v => v.Name, v => v.Value, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Defaults_everywhere_when_nothing_is_set()
    {
        var effective = SettingsResolver.Resolve(ScrubShotSettings.Defaults, InMemoryPolicySource.Empty, InMemoryPolicySource.Empty);

        Assert.Equal(ScrubShotSettings.Defaults, effective.Values);
        Assert.All(effective.Paths, path => Assert.Equal(SettingSource.Default, effective.SourceOf(path)));
        Assert.All(effective.Paths, path => Assert.False(effective.IsLocked(path)));
    }

    [Fact]
    public void User_value_that_differs_from_default_is_reported_as_user()
    {
        var user = ScrubShotSettings.Defaults with { Output = new OutputSettings { Format = ImageFormat.Jpg } };

        var effective = SettingsResolver.Resolve(user, InMemoryPolicySource.Empty, InMemoryPolicySource.Empty);

        Assert.Equal(ImageFormat.Jpg, effective.Values.Output.Format);
        Assert.Equal(SettingSource.User, effective.SourceOf("Output.Format"));
        Assert.Equal(SettingSource.Default, effective.SourceOf("Output.DefaultAction"));
        Assert.False(effective.IsLocked("Output.Format"));
    }

    [Fact]
    public void User_policy_overrides_user_settings_and_locks()
    {
        var user = ScrubShotSettings.Defaults with { Capture = new CaptureSettings { DelayMilliseconds = 500 } };

        var effective = SettingsResolver.Resolve(user, InMemoryPolicySource.Empty, Policy(("Capture.DelayMilliseconds", 0)));

        Assert.Equal(0, effective.Values.Capture.DelayMilliseconds);
        Assert.Equal(SettingSource.UserPolicy, effective.SourceOf("Capture.DelayMilliseconds"));
        Assert.True(effective.IsLocked("Capture.DelayMilliseconds"));
    }

    [Fact]
    public void Machine_policy_overrides_user_policy()
    {
        var effective = SettingsResolver.Resolve(
            ScrubShotSettings.Defaults,
            Policy(("Output.ExcludeFromClipboardHistory", 1)),
            Policy(("Output.ExcludeFromClipboardHistory", 0)));

        Assert.True(effective.Values.Output.ExcludeFromClipboardHistory);
        Assert.Equal(SettingSource.MachinePolicy, effective.SourceOf("Output.ExcludeFromClipboardHistory"));
    }

    [Fact]
    public void Policy_names_are_case_insensitive()
    {
        var effective = SettingsResolver.Resolve(ScrubShotSettings.Defaults, Policy(("general.launchatstartup", 0)), InMemoryPolicySource.Empty);

        Assert.False(effective.Values.General.LaunchAtStartup);
        Assert.Equal(SettingSource.MachinePolicy, effective.SourceOf("General.LaunchAtStartup"));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(42, true)]
    [InlineData("true", true)]
    [InlineData("False", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void Bool_settings_accept_dword_and_string_forms(object raw, bool expected)
    {
        var effective = SettingsResolver.Resolve(ScrubShotSettings.Defaults, Policy(("Capture.CaptureCursor", raw)), InMemoryPolicySource.Empty);

        Assert.Equal(expected, effective.Values.Capture.CaptureCursor);
        Assert.True(effective.IsLocked("Capture.CaptureCursor"));
    }

    [Theory]
    [InlineData(75)]
    [InlineData("75")]
    public void Int_settings_accept_dword_and_string_forms(object raw)
    {
        var effective = SettingsResolver.Resolve(ScrubShotSettings.Defaults, Policy(("Output.JpgQuality", raw)), InMemoryPolicySource.Empty);

        Assert.Equal(75, effective.Values.Output.JpgQuality);
    }

    [Theory]
    [InlineData("CopyAndSave", DefaultCaptureAction.CopyAndSave)]
    [InlineData("save", DefaultCaptureAction.Save)]
    [InlineData(1, DefaultCaptureAction.Save)]
    public void Enum_settings_accept_names_case_insensitively_and_dword_ordinals(object raw, DefaultCaptureAction expected)
    {
        var effective = SettingsResolver.Resolve(ScrubShotSettings.Defaults, Policy(("Output.DefaultAction", raw)), InMemoryPolicySource.Empty);

        Assert.Equal(expected, effective.Values.Output.DefaultAction);
        Assert.True(effective.IsLocked("Output.DefaultAction"));
    }

    [Fact]
    public void String_settings_accept_strings_only()
    {
        var effective = SettingsResolver.Resolve(
            ScrubShotSettings.Defaults,
            Policy(("Output.SaveFolder", @"C:\Captures"), ("Hotkeys.Region", 123)),
            InMemoryPolicySource.Empty);

        Assert.Equal(@"C:\Captures", effective.Values.Output.SaveFolder);
        Assert.Equal(SettingSource.MachinePolicy, effective.SourceOf("Output.SaveFolder"));
        Assert.Equal("PrintScreen", effective.Values.Hotkeys.Region);
        Assert.Equal(SettingSource.Default, effective.SourceOf("Hotkeys.Region"));
    }

    [Theory]
    [InlineData("Output.DefaultAction", "Banana")]
    [InlineData("Output.DefaultAction", 99)]
    [InlineData("Output.DefaultAction", "1")]
    [InlineData("Output.DefaultAction", "Copy,Save")]
    [InlineData("Output.DefaultAction", "Copy, CopyAndSave")]
    [InlineData("Output.JpgQuality", "ninety")]
    [InlineData("Capture.CaptureCursor", "yes")]
    [InlineData("Nope.Nothing", 1)]
    [InlineData("SchemaVersion", 7)]
    [InlineData("Output", 1)]
    public void Invalid_or_unknown_policy_values_are_ignored_and_do_not_lock(string name, object raw)
    {
        var effective = SettingsResolver.Resolve(ScrubShotSettings.Defaults, Policy((name, raw)), InMemoryPolicySource.Empty);

        Assert.Equal(ScrubShotSettings.Defaults, effective.Values);
        Assert.All(effective.Paths, path => Assert.False(effective.IsLocked(path)));
    }

    [Fact]
    public void Invalid_machine_value_does_not_unlock_a_valid_user_policy_value()
    {
        var effective = SettingsResolver.Resolve(
            ScrubShotSettings.Defaults,
            Policy(("Output.Format", "Webp")),
            Policy(("Output.Format", "Jpg")));

        Assert.Equal(ImageFormat.Jpg, effective.Values.Output.Format);
        Assert.Equal(SettingSource.UserPolicy, effective.SourceOf("Output.Format"));
    }

    [Fact]
    public void Null_section_in_user_settings_does_not_break_resolution()
    {
        var user = ScrubShotSettings.Defaults with { Output = null! };

        var effective = SettingsResolver.Resolve(user, Policy(("Output.JpgQuality", 50)), InMemoryPolicySource.Empty);

        Assert.Equal(50, effective.Values.Output.JpgQuality);
        Assert.Equal(ImageFormat.Png, effective.Values.Output.Format);
        Assert.Equal(SettingSource.MachinePolicy, effective.SourceOf("Output.JpgQuality"));
    }

    [Fact]
    public void Unknown_path_throws()
    {
        var effective = SettingsResolver.Resolve(ScrubShotSettings.Defaults, InMemoryPolicySource.Empty, InMemoryPolicySource.Empty);

        Assert.Throws<ArgumentException>(() => effective.SourceOf("Output.Nope"));
    }

    [Fact]
    public void Schema_lists_every_leaf_setting_and_excludes_schema_version()
    {
        var paths = SettingsSchema.All.Select(d => d.Path).ToList();

        Assert.Contains("General.LaunchAtStartup", paths);
        Assert.Contains("Output.FileNamePattern", paths);
        Assert.Contains("Hotkeys.RepeatLastRegion", paths);
        Assert.Contains("Advanced.DebugLogging", paths);
        Assert.DoesNotContain("SchemaVersion", paths);
        Assert.Equal(paths.Count, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(SettingsSchema.All, d => Assert.True(SettingsSchema.TryGet(d.Path.ToUpperInvariant(), out _)));
    }

    [Fact]
    public void Resolved_sources_cover_exactly_the_schema()
    {
        var effective = SettingsResolver.Resolve(ScrubShotSettings.Defaults, InMemoryPolicySource.Empty, InMemoryPolicySource.Empty);

        Assert.Equal(
            SettingsSchema.All.Select(d => d.Path).OrderBy(p => p, StringComparer.Ordinal),
            effective.Paths.OrderBy(p => p, StringComparer.Ordinal));
    }
}
