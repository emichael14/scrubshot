// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Settings;

namespace ScrubShot.Tests.Core;

public sealed class JsonSettingsStoreTests : IDisposable
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

    [Fact]
    public void Missing_file_yields_defaults_and_creates_nothing()
    {
        var store = new JsonSettingsStore(FilePath);

        var loaded = store.Load();

        Assert.Equal(ScrubShotSettings.Defaults, loaded);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void Save_then_load_round_trips_and_leaves_no_temp_files()
    {
        var store = new JsonSettingsStore(FilePath);
        var settings = ScrubShotSettings.Defaults with
        {
            Output = new OutputSettings { Format = ImageFormat.Jpg, JpgQuality = 70, SaveFolder = @"D:\Shots" },
            Hotkeys = new HotkeySettings { Region = "Ctrl+Shift+S" },
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(settings, loaded);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Save_overwrites_an_existing_file_atomically()
    {
        var store = new JsonSettingsStore(FilePath);
        store.Save(ScrubShotSettings.Defaults);
        var changed = ScrubShotSettings.Defaults with { Advanced = new AdvancedSettings { DebugLogging = true } };

        store.Save(changed);

        Assert.True(store.Load().Advanced.DebugLogging);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Corrupt_file_yields_defaults_and_is_set_aside_without_overwriting_earlier_copies()
    {
        Directory.CreateDirectory(_directory);
        var store = new JsonSettingsStore(FilePath);

        File.WriteAllText(FilePath, "{ this is not json");
        var first = store.Load();
        File.WriteAllText(FilePath, "[1, 2");
        var second = store.Load();

        Assert.Equal(ScrubShotSettings.Defaults, first);
        Assert.Equal(ScrubShotSettings.Defaults, second);
        Assert.False(File.Exists(FilePath));
        var setAside = Directory.GetFiles(_directory, "settings.invalid.*.json");
        Assert.Equal(2, setAside.Length);
        Assert.Contains(setAside, f => File.ReadAllText(f) == "{ this is not json");
        Assert.Contains(setAside, f => File.ReadAllText(f) == "[1, 2");
    }

    [Fact]
    public void One_bad_leaf_keeps_every_other_setting()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{ "SchemaVersion": 1, "Output": { "Format": "Webp", "JpgQuality": 55 }, "Advanced": { "DebugLogging": true } }""");
        var store = new JsonSettingsStore(FilePath);

        var loaded = store.Load();

        Assert.Equal(ImageFormat.Png, loaded.Output.Format);
        Assert.Equal(55, loaded.Output.JpgQuality);
        Assert.True(loaded.Advanced.DebugLogging);
        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void Null_section_in_file_loads_and_resolves_with_policy_on_that_section()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{ "SchemaVersion": 1, "Output": null, "Capture": { "DelayMilliseconds": 250 } }""");
        var store = new JsonSettingsStore(FilePath);
        var policy = new InMemoryPolicySource(new Dictionary<string, object> { ["Output.Format"] = "Jpg" });

        var effective = SettingsResolver.Resolve(store.Load(), policy, InMemoryPolicySource.Empty);

        Assert.Equal(ImageFormat.Jpg, effective.Values.Output.Format);
        Assert.Equal(SettingSource.MachinePolicy, effective.SourceOf("Output.Format"));
        Assert.Equal(250, effective.Values.Capture.DelayMilliseconds);
    }

    [Fact]
    public void Unreadable_file_is_set_aside_on_the_next_save_instead_of_overwritten()
    {
        Directory.CreateDirectory(_directory);
        const string original = """{ "SchemaVersion": 1, "Output": { "JpgQuality": 33 } }""";
        File.WriteAllText(FilePath, original);
        var store = new JsonSettingsStore(FilePath);

        ScrubShotSettings loaded;
        using (File.Open(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            loaded = store.Load();
        }

        store.Save(ScrubShotSettings.Defaults);

        Assert.Equal(ScrubShotSettings.Defaults, loaded);
        Assert.Equal(ScrubShotSettings.Defaults, store.Load());
        var setAside = Assert.Single(Directory.GetFiles(_directory, "settings.invalid.*.json"));
        Assert.Equal(original, File.ReadAllText(setAside));
    }

    [Fact]
    public void Newer_schema_file_is_backed_up_before_the_first_save()
    {
        Directory.CreateDirectory(_directory);
        const string newer = """{ "SchemaVersion": 7, "Output": { "JpgQuality": 44 }, "Future": { "X": 1 } }""";
        File.WriteAllText(FilePath, newer);
        var store = new JsonSettingsStore(FilePath);

        var loaded = store.Load();
        store.Save(loaded);
        store.Save(loaded with { Advanced = new AdvancedSettings { DebugLogging = true } });

        Assert.Equal(44, loaded.Output.JpgQuality);
        Assert.Equal(newer, File.ReadAllText(Path.Combine(_directory, "settings.v7.json")));
        Assert.Equal(ScrubShotSettings.CurrentSchemaVersion, store.Load().SchemaVersion);
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    [Fact]
    public void Older_schema_version_is_migrated_to_current()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{ "SchemaVersion": 0, "Output": { "Format": "Jpg" } }""");
        var store = new JsonSettingsStore(FilePath);

        var loaded = store.Load();

        Assert.Equal(ScrubShotSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(ImageFormat.Jpg, loaded.Output.Format);
        Assert.Equal(ScrubShotSettings.Defaults.Capture, loaded.Capture);
    }

    [Fact]
    public void Unknown_properties_and_comments_are_tolerated()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """
            {
              // a comment
              "SchemaVersion": 1,
              "Future": { "Thing": true },
              "General": { "Theme": "Dark", "Unknown": 3, },
            }
            """);
        var store = new JsonSettingsStore(FilePath);

        var loaded = store.Load();

        Assert.Equal(AppTheme.Dark, loaded.General.Theme);
    }

    [Fact]
    public void Enum_names_are_written_as_strings()
    {
        var store = new JsonSettingsStore(FilePath);

        store.Save(ScrubShotSettings.Defaults with { Output = new OutputSettings { DefaultAction = DefaultCaptureAction.CopyAndSave } });

        Assert.Contains("\"CopyAndSave\"", File.ReadAllText(FilePath));
    }
}
