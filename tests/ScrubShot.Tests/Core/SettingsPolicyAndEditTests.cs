// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Text.Json.Nodes;
using ScrubShot.Core.Hotkeys;
using ScrubShot.Core.Settings;

namespace ScrubShot.Tests.Core;

/// <summary>A settings store in memory that counts saves and can refuse them.</summary>
internal sealed class TestSettingsStore(ScrubShotSettings stored) : ISettingsStore
{
    public string FilePath => @"X:\memory\settings.json";

    public ScrubShotSettings Stored { get; set; } = stored;

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

/// <summary>A policy hive whose values can change while the app "runs" and whose reads can be made to fail.</summary>
internal sealed class ScriptedPolicy : IPolicySource
{
    public Dictionary<string, object> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Fail { get; set; }

    public int Reads { get; private set; }

    public IReadOnlyDictionary<string, object> Read()
    {
        Reads++;
        return Fail
            ? throw new PolicyReadException(new IOException("the key is being rewritten"))
            : new Dictionary<string, object>(Values, StringComparer.OrdinalIgnoreCase);
    }
}

public class SettingsPolicyRefreshTests
{
    private const string Pictures = @"C:\Users\x\Pictures";
    private const string Exclusion = "Output.ExcludeFromClipboardHistory";

    private static (SettingsService Service, TestSettingsStore Store, ScriptedPolicy Machine, List<EffectiveSettings> Announced) Create(ScrubShotSettings? stored = null)
    {
        var store = new TestSettingsStore(stored ?? ScrubShotSettings.Defaults);
        var machine = new ScriptedPolicy();
        var service = new SettingsService(store, machine, InMemoryPolicySource.Empty, () => Pictures);
        var announced = new List<EffectiveSettings>();
        service.Changed += announced.Add;
        return (service, store, machine, announced);
    }

    private static ScrubShotSettings ExclusionOff => ScrubShotSettings.Defaults with { Output = new OutputSettings { ExcludeFromClipboardHistory = false } };

    [Fact]
    public void A_failed_policy_read_keeps_the_locks_already_known()
    {
        var (service, _, machine, _) = Create(ExclusionOff);
        machine.Values[Exclusion] = 1;
        service.Reload();
        Assert.True(service.Effective.IsLocked(Exclusion));

        machine.Fail = true; // a policy refresh is rewriting the key
        service.RefreshPolicy();
        var result = service.TryChange(service.Effective.Values with { Capture = new CaptureSettings { ShowMagnifier = false } });

        Assert.True(result.Saved);
        Assert.True(service.Effective.IsLocked(Exclusion));                       // never fails open
        Assert.True(service.Effective.Values.Output.ExcludeFromClipboardHistory); // the user's stored "false" stays overridden
        Assert.False(service.Effective.Values.Capture.ShowMagnifier);
    }

    [Fact]
    public void When_policy_cannot_be_read_at_first_start_the_app_runs_and_the_next_refresh_applies_it()
    {
        var (service, _, machine, announced) = Create(ExclusionOff);
        machine.Values[Exclusion] = 1;
        machine.Fail = true;

        service.Reload();

        Assert.Equal(SettingsService.FirstReadAttempts, machine.Reads); // retried, then started without it
        Assert.False(service.Effective.IsLocked(Exclusion));

        machine.Fail = false;
        announced.Clear();
        service.RefreshPolicy(); // the app does this at the start of every capture

        Assert.True(service.Effective.IsLocked(Exclusion));
        Assert.True(service.Effective.Values.Output.ExcludeFromClipboardHistory);
        Assert.Single(announced);
    }

    [Fact]
    public void A_policy_pushed_while_running_takes_effect_at_the_next_refresh_and_is_announced_once()
    {
        var (service, store, machine, announced) = Create(ExclusionOff);
        service.Reload();
        announced.Clear();
        Assert.False(service.Effective.Values.Output.ExcludeFromClipboardHistory);

        machine.Values[Exclusion] = 1;
        var refreshed = service.RefreshPolicy();
        var again = service.RefreshPolicy();

        Assert.True(refreshed.Values.Output.ExcludeFromClipboardHistory);
        Assert.True(refreshed.IsLocked(Exclusion));
        Assert.Same(refreshed, again);   // nothing new the second time
        Assert.Single(announced);
        Assert.Equal(0, store.Saves);    // a refresh never writes the user's file
        Assert.False(store.Stored.Output.ExcludeFromClipboardHistory);
    }

    [Fact]
    public void A_lock_that_arrived_since_the_last_read_already_blocks_the_change()
    {
        var (service, store, machine, announced) = Create();
        service.Reload();
        announced.Clear();

        machine.Values[Exclusion] = 1; // pushed after the Settings window was opened
        var result = service.TryChange(service.Effective.Values with { Output = new OutputSettings { ExcludeFromClipboardHistory = false } });

        Assert.Equal(SettingsChangeOutcome.NoChange, result.Outcome);
        Assert.Equal(0, store.Saves);
        Assert.True(service.Effective.IsLocked(Exclusion));
        Assert.Single(announced); // the new lock itself is announced so the window can show it
    }

    [Fact]
    public void Reload_picks_up_a_file_changed_outside_the_app_and_announces_it()
    {
        var (service, store, _, announced) = Create();
        service.Reload();
        announced.Clear();

        store.Stored = ScrubShotSettings.Defaults with { Capture = new CaptureSettings { DelayMilliseconds = 750 } };
        service.Reload();
        service.Reload();

        Assert.Equal(750, service.Effective.Values.Capture.DelayMilliseconds);
        Assert.Single(announced);
    }

    [Fact]
    public void A_hotkey_set_by_policy_keeps_its_gesture_and_the_unlocked_action_is_the_duplicate()
    {
        var settings = new HotkeySettings { FullScreen = "PrintScreen" }; // Region is PrintScreen by default
        var sources = new Dictionary<HotkeyAction, SettingSource> { [HotkeyAction.FullScreen] = SettingSource.MachinePolicy };

        var withPolicy = HotkeyBindings.Precheck(settings, sources);
        var withoutPolicy = HotkeyBindings.Precheck(settings);

        Assert.Equal([HotkeyAction.Region, HotkeyAction.Window, HotkeyAction.FullScreen, HotkeyAction.RepeatLastRegion], withPolicy.Select(c => c.Action));
        Assert.Equal(HotkeyConflict.DuplicateInSettings, withPolicy[0].Conflict);
        Assert.True(withPolicy[2].CanRegister);
        Assert.True(withoutPolicy[0].CanRegister); // no policy: the earlier action keeps it, as before
        Assert.Equal(HotkeyConflict.DuplicateInSettings, withoutPolicy[2].Conflict);
    }

    [Fact]
    public void Machine_policy_beats_user_policy_for_the_same_gesture()
    {
        var settings = new HotkeySettings { Region = "Ctrl+Shift+S", RepeatLastRegion = "Ctrl+Shift+S" };
        var sources = new Dictionary<HotkeyAction, SettingSource>
        {
            [HotkeyAction.Region] = SettingSource.UserPolicy,
            [HotkeyAction.RepeatLastRegion] = SettingSource.MachinePolicy,
        };

        var checks = HotkeyBindings.Precheck(settings, sources);

        Assert.Equal(HotkeyConflict.DuplicateInSettings, checks[0].Conflict);
        Assert.True(checks[3].CanRegister);
    }
}

public class SettingsEditSessionTests
{
    private const string Pictures = @"C:\Users\x\Pictures";

    private static (SettingsEditSession Session, SettingsService Service, TestSettingsStore Store, ScriptedPolicy Machine) Create(ScrubShotSettings? stored = null, Action<ScriptedPolicy>? policy = null)
    {
        var store = new TestSettingsStore(stored ?? ScrubShotSettings.Defaults);
        var machine = new ScriptedPolicy();
        policy?.Invoke(machine);
        var service = new SettingsService(store, machine, InMemoryPolicySource.Empty, () => Pictures);
        service.Reload();
        return (new SettingsEditSession(() => service.Effective, service.TryChange), service, store, machine);
    }

    private static Func<ScrubShotSettings, ScrubShotSettings> Region(string gesture) => v => v with { Hotkeys = v.Hotkeys with { Region = gesture } };

    private static Func<ScrubShotSettings, ScrubShotSettings> Window(string gesture) => v => v with { Hotkeys = v.Hotkeys with { Window = gesture } };

    [Fact]
    public void A_refused_value_stays_pending_with_its_reason_and_is_not_saved()
    {
        var (session, service, store, _) = Create();

        var outcome = session.Change("Hotkeys.Region", Region("Banana"), "Banana");

        Assert.Equal(SettingsChangeOutcome.Invalid, outcome);
        Assert.Equal("Banana", session.PendingText("Hotkeys.Region"));
        Assert.Equal(SettingProblem.HotkeyInvalid, session.ProblemFor("Hotkeys.Region"));
        Assert.Equal("PrintScreen", service.Effective.Values.Hotkeys.Region);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void A_pending_value_is_saved_once_another_change_removes_what_was_wrong_with_it()
    {
        var (session, service, _, _) = Create();
        session.Change("Hotkeys.Region", Region("Alt+PrintScreen"), "Alt+PrintScreen"); // Window's default: a duplicate
        Assert.Equal(SettingProblem.HotkeyDuplicate, session.ProblemFor("Hotkeys.Region"));

        var outcome = session.Change("Hotkeys.Window", Window("Ctrl+Alt+W"), "Ctrl+Alt+W");

        Assert.Equal(SettingsChangeOutcome.Saved, outcome);
        Assert.False(session.HasPending("Hotkeys.Region"));               // what is on screen is now what is in effect
        Assert.Equal("Alt+PrintScreen", service.Effective.Values.Hotkeys.Region);
        Assert.Equal("Ctrl+Alt+W", service.Effective.Values.Hotkeys.Window);
    }

    [Fact]
    public void Committing_the_same_refused_text_again_tries_it_again()
    {
        var (session, service, _, _) = Create();
        session.Change("Hotkeys.Region", Region("Alt+PrintScreen"), "Alt+PrintScreen");
        service.TryChange(service.Effective.Values with { Hotkeys = service.Effective.Values.Hotkeys with { Window = "Ctrl+Alt+W" } }); // fixed elsewhere

        var outcome = session.Change("Hotkeys.Region", Region("Alt+PrintScreen"), "Alt+PrintScreen");

        Assert.Equal(SettingsChangeOutcome.Saved, outcome);
        Assert.False(session.HasPending("Hotkeys.Region"));
    }

    [Fact]
    public void A_pending_value_that_is_still_wrong_keeps_its_current_reason()
    {
        var (session, _, _, _) = Create();
        session.Change("Hotkeys.Region", Region("Banana"), "Banana");

        session.Change("Capture.ShowMagnifier", v => v with { Capture = v.Capture with { ShowMagnifier = false } });

        Assert.Equal("Banana", session.PendingText("Hotkeys.Region"));
        Assert.Equal(SettingProblem.HotkeyInvalid, session.ProblemFor("Hotkeys.Region"));
    }

    [Fact]
    public void Putting_the_saved_value_back_forgets_the_pending_one()
    {
        var (session, _, _, _) = Create();
        session.Change("Hotkeys.Region", Region("Banana"), "Banana");

        session.Revert("Hotkeys.Region");

        Assert.Null(session.PendingText("Hotkeys.Region"));
        Assert.Null(session.ProblemFor("Hotkeys.Region"));
    }

    [Fact]
    public void An_edit_to_a_locked_setting_is_dropped_and_leaves_nothing_pending()
    {
        var (session, service, store, _) = Create(policy: p => p.Values["Hotkeys.Region"] = "Ctrl+Shift+R");

        var outcome = session.Change("Hotkeys.Region", Region("Ctrl+Shift+X"), "Ctrl+Shift+X");

        Assert.Equal(SettingsChangeOutcome.NoChange, outcome);
        Assert.False(session.HasPending("Hotkeys.Region"));
        Assert.Equal("Ctrl+Shift+R", service.Effective.Values.Hotkeys.Region);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void A_failed_write_is_flagged_until_the_next_edit()
    {
        var (session, _, store, _) = Create();
        store.ThrowOnSave = true;

        session.Change("Capture.CaptureCursor", v => v with { Capture = v.Capture with { CaptureCursor = true } });
        Assert.True(session.SaveFailed);

        store.ThrowOnSave = false;
        session.Change("Capture.CaptureCursor", v => v with { Capture = v.Capture with { CaptureCursor = true } });
        Assert.False(session.SaveFailed);
    }

    [Fact]
    public void Reset_restores_the_four_defaults_and_clears_pending_hotkeys()
    {
        var stored = ScrubShotSettings.Defaults with { Hotkeys = new HotkeySettings { Region = "Ctrl+Shift+1", Window = "Ctrl+Shift+2" } };
        var (session, service, _, _) = Create(stored);
        session.Change("Hotkeys.FullScreen", v => v with { Hotkeys = v.Hotkeys with { FullScreen = "Banana" } }, "Banana");

        var outcome = session.ResetHotkeys();

        Assert.Equal(SettingsChangeOutcome.Saved, outcome);
        Assert.Equal(new HotkeySettings(), service.Effective.Values.Hotkeys);
        Assert.False(session.HasPending("Hotkeys.FullScreen"));
        Assert.False(session.ResetWasPartial);
    }

    [Fact]
    public void Reset_leaves_a_hotkey_whose_default_is_taken_by_a_policy_hotkey_and_resets_the_rest()
    {
        // Policy fixes Region to the gesture that is FullScreen's default.
        var stored = ScrubShotSettings.Defaults with { Hotkeys = new HotkeySettings { Window = "Ctrl+Alt+W", FullScreen = "Ctrl+Shift+F" } };
        var (session, service, _, _) = Create(stored, p => p.Values["Hotkeys.Region"] = "Ctrl+PrintScreen");

        var outcome = session.ResetHotkeys();

        var hotkeys = service.Effective.Values.Hotkeys;
        Assert.Equal(SettingsChangeOutcome.Saved, outcome);
        Assert.Equal("Ctrl+PrintScreen", hotkeys.Region);       // policy
        Assert.Equal("Alt+PrintScreen", hotkeys.Window);        // reset
        Assert.Equal("Ctrl+Shift+F", hotkeys.FullScreen);       // left alone: its default belongs to the policy hotkey
        Assert.True(session.ResetWasPartial);
    }

    [Fact]
    public void Pending_values_for_settings_that_became_locked_are_dropped()
    {
        var (session, service, _, machine) = Create();
        session.Change("Hotkeys.Region", Region("Banana"), "Banana");

        machine.Values["Hotkeys.Region"] = "Ctrl+Shift+R";
        service.RefreshPolicy();
        session.OnSettingsChangedElsewhere();

        Assert.False(session.HasPending("Hotkeys.Region"));
    }
}

/// <summary>The settings file itself: a save writes differences and leaves everything else in the file alone.</summary>
public sealed class JsonSettingsStorePatchTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ScrubShotTests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonObject ReadFile() => JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();

    [Fact]
    public void A_save_changes_only_the_edited_setting_and_keeps_the_rest_of_the_file()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """
            {
              // written by hand
              "SchemaVersion": 1,
              "output": { "Format": "Jpeg", "JpgQuality": "85" },
              "Mine": { "Keep": 1 }
            }
            """);
        var store = new JsonSettingsStore(FilePath);
        var loaded = store.Load();
        Assert.Equal(ImageFormat.Png, loaded.Output.Format); // "Jpeg" is not a valid name; the default applies

        store.Save(loaded with { Capture = loaded.Capture with { ShowMagnifier = false } });

        var file = ReadFile();
        Assert.Equal("Jpeg", (string?)file["output"]!["Format"]);        // what the user wrote is still there
        Assert.Equal("85", (string?)file["output"]!["JpgQuality"]);
        Assert.Equal(1, (int?)file["Mine"]!["Keep"]);                    // a property this version does not know
        Assert.False((bool)file["Capture"]!["ShowMagnifier"]!);
        Assert.Single(file["Capture"]!.AsObject());                      // only the edited setting
        Assert.False(file.ContainsKey("General"));                       // never mentioned, still not mentioned
        Assert.False(file.ContainsKey("Output"));                        // no second spelling of the section
        Assert.Single(Directory.GetFiles(_directory));
        Assert.False(store.Load().Capture.ShowMagnifier);
    }

    [Fact]
    public void The_first_save_without_a_file_writes_only_what_differs_from_the_defaults()
    {
        var store = new JsonSettingsStore(FilePath);
        var loaded = store.Load();

        store.Save(loaded with { Output = loaded.Output with { DefaultAction = DefaultCaptureAction.CopyAndSave } });

        var file = ReadFile();
        Assert.Equal(["SchemaVersion", "Output"], file.Select(p => p.Key));
        Assert.Equal("CopyAndSave", (string?)file["Output"]!["DefaultAction"]);
        Assert.Single(file["Output"]!.AsObject());
    }

    [Fact]
    public void A_setting_spelled_in_another_case_is_replaced_in_place()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{ "output": { "format": "Jpg" } }""");
        var store = new JsonSettingsStore(FilePath);
        var loaded = store.Load();
        Assert.Equal(ImageFormat.Jpg, loaded.Output.Format);

        store.Save(loaded with { Output = loaded.Output with { Format = ImageFormat.Png } });

        var output = ReadFile()["output"]!.AsObject();
        Assert.Equal("Png", (string?)Assert.Single(output).Value);
        Assert.Equal("format", Assert.Single(output).Key);
        Assert.Equal(ImageFormat.Png, store.Load().Output.Format);
    }

    [Fact]
    public void A_section_that_was_not_an_object_is_replaced_when_one_of_its_settings_changes()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{ "SchemaVersion": 1, "Capture": null }""");
        var store = new JsonSettingsStore(FilePath);
        var loaded = store.Load();

        store.Save(loaded with { Capture = loaded.Capture with { DelayMilliseconds = 500 } });

        Assert.Equal(500, (int?)ReadFile()["Capture"]!["DelayMilliseconds"]);
        Assert.Equal(500, store.Load().Capture.DelayMilliseconds);
    }

    [Fact]
    public void Two_saves_in_a_row_each_write_their_own_difference()
    {
        var store = new JsonSettingsStore(FilePath);
        var loaded = store.Load();
        var first = loaded with { Capture = loaded.Capture with { CaptureCursor = true } };
        store.Save(first);

        store.Save(first with { Capture = first.Capture with { CaptureCursor = false, DelayMilliseconds = 250 } });

        var capture = ReadFile()["Capture"]!.AsObject();
        Assert.False((bool)capture["CaptureCursor"]!); // set back explicitly: the user chose it
        Assert.Equal(250, (int?)capture["DelayMilliseconds"]);
        Assert.Equal(2, capture.Count);
    }

    [Fact]
    public void Through_the_service_a_change_writes_one_setting_and_never_a_policy_value()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{ "SchemaVersion": 1, "Output": { "ExcludeFromClipboardHistory": false } }""");
        var policy = new ScriptedPolicy();
        policy.Values["Output.ExcludeFromClipboardHistory"] = 1;
        var service = new SettingsService(new JsonSettingsStore(FilePath), policy, InMemoryPolicySource.Empty, () => @"C:\Users\x\Pictures");
        service.Reload();

        var result = service.TryChange(service.Effective.Values with { General = new GeneralSettings { Theme = AppTheme.Dark } });

        var file = ReadFile();
        Assert.True(result.Saved);
        Assert.Equal("Dark", (string?)file["General"]!["Theme"]);
        Assert.Single(file["General"]!.AsObject());
        Assert.False((bool)file["Output"]!["ExcludeFromClipboardHistory"]!); // the user's own value, not the policy's
        Assert.True(service.Effective.Values.Output.ExcludeFromClipboardHistory);
    }
}
