// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Hotkeys;
using ScrubShot.Core.Settings;

namespace ScrubShot.Tests.Core;

public class SettingsValidatorTests
{
    private const string Pictures = @"C:\Users\x\Pictures";

    [Fact]
    public void Defaults_are_valid()
    {
        Assert.Empty(SettingsValidator.Validate(ScrubShotSettings.Defaults, Pictures));
    }

    [Theory]
    [InlineData("Banana", SettingProblem.HotkeyInvalid)]
    [InlineData("", SettingProblem.HotkeyInvalid)]
    [InlineData("Ctrl+F12", SettingProblem.HotkeyReservedKey)]
    [InlineData("A", SettingProblem.HotkeyNeedsModifier)]
    public void Hotkey_problems_are_reported_on_the_action(string gesture, SettingProblem expected)
    {
        var settings = ScrubShotSettings.Defaults with { Hotkeys = new HotkeySettings { FullScreen = gesture } };

        var issue = Assert.Single(SettingsValidator.Validate(settings, Pictures));

        Assert.Equal("Hotkeys.FullScreen", issue.Path);
        Assert.Equal(expected, issue.Problem);
    }

    [Fact]
    public void A_duplicate_gesture_is_reported_on_every_action_that_shares_it()
    {
        var settings = ScrubShotSettings.Defaults with { Hotkeys = new HotkeySettings { Region = "Ctrl+Shift+S", RepeatLastRegion = "ctrl+shift+s" } };

        var issues = SettingsValidator.Validate(settings, Pictures);

        Assert.Equal(["Hotkeys.Region", "Hotkeys.RepeatLastRegion"], issues.Select(i => i.Path).Order());
        Assert.All(issues, i => Assert.Equal(SettingProblem.HotkeyDuplicate, i.Problem));
    }

    [Theory]
    [InlineData(@"\Shots", Pictures, SettingProblem.SaveFolderNotFullyQualified)]
    [InlineData("Shots", "", SettingProblem.SaveFolderPicturesUnavailable)]
    [InlineData(@"%SCRUBSHOT_NOT_DEFINED%\x", Pictures, SettingProblem.SaveFolderUnresolvedVariable)]
    public void Save_folder_problems_are_reported(string folder, string pictures, SettingProblem expected)
    {
        var settings = ScrubShotSettings.Defaults with { Output = new OutputSettings { SaveFolder = folder } };

        var issue = Assert.Single(SettingsValidator.Validate(settings, pictures));

        Assert.Equal("Output.SaveFolder", issue.Path);
        Assert.Equal(expected, issue.Problem);
    }

    [Theory]
    [InlineData(0, 0, SettingProblem.JpgQualityOutOfRange, "Output.JpgQuality")]
    [InlineData(101, 0, SettingProblem.JpgQualityOutOfRange, "Output.JpgQuality")]
    [InlineData(90, -1, SettingProblem.DelayOutOfRange, "Capture.DelayMilliseconds")]
    [InlineData(90, 10_001, SettingProblem.DelayOutOfRange, "Capture.DelayMilliseconds")]
    public void Numbers_outside_their_range_are_reported(int quality, int delay, SettingProblem expected, string path)
    {
        var settings = ScrubShotSettings.Defaults with
        {
            Output = new OutputSettings { JpgQuality = quality },
            Capture = new CaptureSettings { DelayMilliseconds = delay },
        };

        var issue = Assert.Single(SettingsValidator.Validate(settings, Pictures));

        Assert.Equal(path, issue.Path);
        Assert.Equal(expected, issue.Problem);
    }

    [Fact]
    public void Precheck_keeps_the_first_holder_of_a_gesture_and_marks_later_ones()
    {
        var checks = HotkeyBindings.Precheck(new HotkeySettings { Region = "F9", Window = "f9", FullScreen = "Enter", RepeatLastRegion = "Nope" });

        Assert.Equal(
            [HotkeyConflict.None, HotkeyConflict.DuplicateInSettings, HotkeyConflict.NeedsModifier, HotkeyConflict.InvalidGesture],
            checks.Select(c => c.Conflict));
        Assert.True(checks[0].CanRegister);
        Assert.False(checks[1].CanRegister);
        Assert.Null(checks[3].Gesture);
    }
}

public class SettingsServiceTests
{
    private const string Pictures = @"C:\Users\x\Pictures";

    [Fact]
    public void Reload_resolves_the_users_file_under_policy()
    {
        var h = new Harness(
            stored: ScrubShotSettings.Defaults with { Capture = new CaptureSettings { ShowMagnifier = false } },
            machine: ("General.LaunchAtStartup", 0));

        var effective = h.Service.Reload();

        Assert.False(effective.Values.Capture.ShowMagnifier);
        Assert.False(effective.Values.General.LaunchAtStartup);
        Assert.True(effective.IsLocked("General.LaunchAtStartup"));
        Assert.Same(effective, h.Service.Effective);
    }

    [Fact]
    public void Before_the_first_reload_defaults_are_in_effect()
    {
        var h = new Harness(stored: ScrubShotSettings.Defaults with { Capture = new CaptureSettings { CaptureCursor = true } });

        Assert.False(h.Service.Effective.Values.Capture.CaptureCursor);
    }

    [Fact]
    public void A_valid_change_is_saved_takes_effect_and_is_announced_once()
    {
        var h = new Harness();
        h.Service.Reload();

        var result = h.Service.TryChange(h.Service.Effective.Values with { Capture = new CaptureSettings { DelayMilliseconds = 500 } });

        Assert.Equal(SettingsChangeOutcome.Saved, result.Outcome);
        Assert.Equal(1, h.Store.Saves);
        Assert.Equal(500, h.Store.Stored.Capture.DelayMilliseconds);
        Assert.Equal(500, h.Service.Effective.Values.Capture.DelayMilliseconds);
        Assert.Same(h.Service.Effective, Assert.Single(h.Announced));
    }

    [Fact]
    public void Only_the_changed_setting_is_written_the_rest_of_the_file_is_untouched()
    {
        var stored = ScrubShotSettings.Defaults with
        {
            Output = new OutputSettings { Format = ImageFormat.Jpg, JpgQuality = 70 },
            Hotkeys = new HotkeySettings { Window = "Banana" }, // an old bad value somewhere else
        };
        var h = new Harness(stored);
        h.Service.Reload();

        var result = h.Service.TryChange(h.Service.Effective.Values with { Capture = new CaptureSettings { ShowCoordinates = false } });

        Assert.True(result.Saved); // the bad hotkey did not change, so it does not block this edit
        Assert.Equal(stored with { Capture = new CaptureSettings { ShowCoordinates = false } }, h.Store.Stored);
    }

    [Fact]
    public void A_proposal_equal_to_the_current_values_writes_nothing()
    {
        var h = new Harness();
        h.Service.Reload();

        var result = h.Service.TryChange(h.Service.Effective.Values);

        Assert.Equal(SettingsChangeOutcome.NoChange, result.Outcome);
        Assert.Equal(0, h.Store.Saves);
        Assert.Empty(h.Announced);
    }

    [Fact]
    public void An_edit_to_a_locked_setting_is_dropped()
    {
        var h = new Harness(user: ("Output.ExcludeFromClipboardHistory", 1));
        h.Service.Reload();

        var result = h.Service.TryChange(h.Service.Effective.Values with { Output = new OutputSettings { ExcludeFromClipboardHistory = false } });

        Assert.Equal(SettingsChangeOutcome.NoChange, result.Outcome);
        Assert.Equal(0, h.Store.Saves);
        Assert.True(h.Service.Effective.Values.Output.ExcludeFromClipboardHistory);
    }

    [Fact]
    public void A_locked_edit_bundled_with_an_allowed_one_saves_only_the_allowed_one()
    {
        var h = new Harness(machine: ("Output.ExcludeFromClipboardHistory", 1));
        h.Service.Reload();
        var proposed = h.Service.Effective.Values with
        {
            Output = new OutputSettings { ExcludeFromClipboardHistory = false, DefaultAction = DefaultCaptureAction.CopyAndSave },
        };

        var result = h.Service.TryChange(proposed);

        Assert.True(result.Saved);
        Assert.Equal(DefaultCaptureAction.CopyAndSave, h.Store.Stored.Output.DefaultAction);
        Assert.True(h.Store.Stored.Output.ExcludeFromClipboardHistory);          // the locked edit never reached the file
        Assert.True(h.Service.Effective.Values.Output.ExcludeFromClipboardHistory);
    }

    [Fact]
    public void The_users_own_value_for_a_locked_setting_survives_other_changes()
    {
        var stored = ScrubShotSettings.Defaults with { General = new GeneralSettings { LaunchAtStartup = true, Theme = AppTheme.System } };
        var h = new Harness(stored, machine: ("General.LaunchAtStartup", 0));
        h.Service.Reload();
        Assert.False(h.Service.Effective.Values.General.LaunchAtStartup);

        var result = h.Service.TryChange(h.Service.Effective.Values with { General = h.Service.Effective.Values.General with { Theme = AppTheme.Dark } });

        Assert.True(result.Saved);
        Assert.True(h.Store.Stored.General.LaunchAtStartup);                      // not overwritten with the policy value
        Assert.Equal(AppTheme.Dark, h.Store.Stored.General.Theme);
        Assert.False(h.Service.Effective.Values.General.LaunchAtStartup);         // policy still wins
    }

    [Theory]
    [InlineData("Banana", SettingProblem.HotkeyInvalid)]
    [InlineData("Alt+PrintScreen", SettingProblem.HotkeyDuplicate)] // the default for Window
    [InlineData("Space", SettingProblem.HotkeyNeedsModifier)]
    public void An_invalid_changed_value_is_refused_and_nothing_is_written(string gesture, SettingProblem expected)
    {
        var h = new Harness();
        h.Service.Reload();
        var before = h.Service.Effective;

        var result = h.Service.TryChange(before.Values with { Hotkeys = before.Values.Hotkeys with { Region = gesture } });

        Assert.Equal(SettingsChangeOutcome.Invalid, result.Outcome);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("Hotkeys.Region", issue.Path);
        Assert.Equal(expected, issue.Problem);
        Assert.Equal(0, h.Store.Saves);
        Assert.Same(before, h.Service.Effective);
        Assert.Empty(h.Announced);
    }

    [Fact]
    public void A_save_folder_that_depends_on_the_current_directory_is_refused()
    {
        var h = new Harness();
        h.Service.Reload();

        var result = h.Service.TryChange(h.Service.Effective.Values with { Output = new OutputSettings { SaveFolder = @"\Shots" } });

        Assert.Equal(SettingsChangeOutcome.Invalid, result.Outcome);
        Assert.Equal(SettingProblem.SaveFolderNotFullyQualified, Assert.Single(result.Issues).Problem);
    }

    [Fact]
    public void A_failed_write_changes_nothing()
    {
        var h = new Harness();
        h.Service.Reload();
        var before = h.Service.Effective;
        h.Store.ThrowOnSave = true;

        var result = h.Service.TryChange(before.Values with { Capture = new CaptureSettings { CaptureCursor = true } });

        Assert.Equal(SettingsChangeOutcome.SaveFailed, result.Outcome);
        Assert.Same(before, h.Service.Effective);
        Assert.Empty(h.Announced);
    }

    [Fact]
    public void Policy_is_read_again_after_a_change()
    {
        var policy = new MutablePolicy();
        var store = new MemoryStore(ScrubShotSettings.Defaults);
        var service = new SettingsService(store, policy, InMemoryPolicySource.Empty, () => Pictures);
        service.Reload();
        Assert.False(service.Effective.IsLocked("Capture.CaptureCursor"));

        policy.Values["Capture.CaptureCursor"] = 1; // an administrator pushes a policy while the app runs
        service.TryChange(service.Effective.Values with { Capture = new CaptureSettings { ShowMagnifier = false } });

        Assert.True(service.Effective.IsLocked("Capture.CaptureCursor"));
        Assert.True(service.Effective.Values.Capture.CaptureCursor);
        Assert.False(service.Effective.Values.Capture.ShowMagnifier);
    }

    [Fact]
    public void Current_values_can_be_validated_for_display()
    {
        var h = new Harness(ScrubShotSettings.Defaults with { Hotkeys = new HotkeySettings { Window = "Banana" } });
        h.Service.Reload();

        var issue = Assert.Single(h.Service.ValidateCurrent());

        Assert.Equal("Hotkeys.Window", issue.Path);
    }

    private sealed class Harness
    {
        public Harness(ScrubShotSettings? stored = null, (string Name, object Value)? machine = null, (string Name, object Value)? user = null)
        {
            Store = new MemoryStore(stored ?? ScrubShotSettings.Defaults);
            Service = new SettingsService(Store, Policy(machine), Policy(user), () => Pictures);
            Service.Changed += Announced.Add;
        }

        public MemoryStore Store { get; }

        public SettingsService Service { get; }

        public List<EffectiveSettings> Announced { get; } = [];

        private static InMemoryPolicySource Policy((string Name, object Value)? value) =>
            value is { } v
                ? new InMemoryPolicySource(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { [v.Name] = v.Value })
                : InMemoryPolicySource.Empty;
    }

    private sealed class MemoryStore(ScrubShotSettings stored) : ISettingsStore
    {
        public string FilePath => @"X:\memory\settings.json";

        public ScrubShotSettings Stored { get; private set; } = stored;

        public int Saves { get; private set; }

        public bool ThrowOnSave { get; set; }

        public ScrubShotSettings Load() => Stored;

        public void Save(ScrubShotSettings settings)
        {
            if (ThrowOnSave)
            {
                throw new IOException("disk full");
            }

            Stored = settings;
            Saves++;
        }
    }

    private sealed class MutablePolicy : IPolicySource
    {
        public Dictionary<string, object> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, object> Read() => new Dictionary<string, object>(Values, StringComparer.OrdinalIgnoreCase);
    }
}
