// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Text.Json;
using System.Text.Json.Nodes;
using ScrubShot.Core.Settings;

namespace ScrubShot.Tests.Core;

public class SettingsDocumentTests
{
    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void Empty_document_becomes_defaults()
    {
        var root = Parse("{}");

        var reset = SettingsDocument.Normalize(root);

        Assert.Equal(ScrubShotSettings.Defaults, root.Deserialize<ScrubShotSettings>(SettingsJson.Options));
        Assert.Equal(0, reset);
    }

    [Fact]
    public void Null_section_and_null_leaf_are_reset_and_other_values_survive()
    {
        var root = Parse("""{ "Output": null, "Hotkeys": { "Region": null, "Window": "F9" }, "General": { "Theme": "Dark" } }""");

        var reset = SettingsDocument.Normalize(root);
        var settings = root.Deserialize<ScrubShotSettings>(SettingsJson.Options)!;

        Assert.Equal(ScrubShotSettings.Defaults.Output, settings.Output);
        Assert.Equal("PrintScreen", settings.Hotkeys.Region);
        Assert.Equal("F9", settings.Hotkeys.Window);
        Assert.Equal(AppTheme.Dark, settings.General.Theme);
        Assert.Equal(2, reset);
    }

    [Theory]
    [InlineData("""{ "Output": { "Format": 99 } }""")]
    [InlineData("""{ "Output": { "Format": "Webp" } }""")]
    [InlineData("""{ "Output": { "Format": "Png,Jpg" } }""")]
    [InlineData("""{ "Output": { "Format": true } }""")]
    public void Bad_enum_leaf_is_reset_to_default(string json)
    {
        var root = Parse(json);

        SettingsDocument.Normalize(root);

        Assert.Equal(ImageFormat.Png, root.Deserialize<ScrubShotSettings>(SettingsJson.Options)!.Output.Format);
    }

    [Fact]
    public void Enum_name_case_is_canonicalized()
    {
        var root = Parse("""{ "Output": { "Format": "jpg" } }""");

        SettingsDocument.Normalize(root);

        Assert.Equal("Jpg", root["Output"]!["Format"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("""{ "Output": { "JpgQuality": "90" } }""")]
    [InlineData("""{ "Output": { "JpgQuality": 1.5 } }""")]
    [InlineData("""{ "Capture": { "CaptureCursor": 1 } }""")]
    [InlineData("""{ "Capture": { "CaptureCursor": "true" } }""")]
    [InlineData("""{ "Output": { "SaveFolder": 7 } }""")]
    [InlineData("""{ "Output": { "SaveFolder": { "x": 1 } } }""")]
    public void Wrong_json_kind_is_reset_to_default(string json)
    {
        var root = Parse(json);

        var reset = SettingsDocument.Normalize(root);

        Assert.Equal(1, reset);
        Assert.Equal(ScrubShotSettings.Defaults, root.Deserialize<ScrubShotSettings>(SettingsJson.Options));
    }

    [Fact]
    public void Property_names_are_matched_case_insensitively_and_canonicalized()
    {
        var root = Parse("""{ "schemaversion": 1, "output": { "format": "Jpg", "jpgquality": 42 } }""");

        var reset = SettingsDocument.Normalize(root);
        var settings = root.Deserialize<ScrubShotSettings>(SettingsJson.Options)!;

        Assert.Equal(ImageFormat.Jpg, settings.Output.Format);
        Assert.Equal(42, settings.Output.JpgQuality);
        Assert.True(root.ContainsKey("Output"));
        Assert.False(root.ContainsKey("output"));
        Assert.True(root["Output"]!.AsObject().ContainsKey("Format"));
        Assert.Equal(0, reset);
    }

    [Fact]
    public void Unknown_properties_are_left_alone()
    {
        var root = Parse("""{ "Future": 1, "Output": { "Later": "x" } }""");

        SettingsDocument.Normalize(root);

        Assert.True(root.ContainsKey("Future"));
        Assert.True(root["Output"]!.AsObject().ContainsKey("Later"));
    }

    [Fact]
    public void Schema_version_is_defaulted_when_missing_or_malformed()
    {
        var missing = Parse("{}");
        var malformed = Parse("""{ "SchemaVersion": "one" }""");

        SettingsDocument.Normalize(missing);
        SettingsDocument.Normalize(malformed);

        Assert.Equal(ScrubShotSettings.CurrentSchemaVersion, missing["SchemaVersion"]!.GetValue<int>());
        Assert.Equal(ScrubShotSettings.CurrentSchemaVersion, malformed["SchemaVersion"]!.GetValue<int>());
    }
}
