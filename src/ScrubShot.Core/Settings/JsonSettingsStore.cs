// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScrubShot.Core.Logging;

namespace ScrubShot.Core.Settings;

public interface ISettingsStore
{
    string FilePath { get; }

    /// <summary>Loads the user's settings. Never throws: a missing or unreadable file yields defaults.</summary>
    ScrubShotSettings Load();

    /// <summary>Writes the settings atomically. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> if the file cannot be written safely.</summary>
    void Save(ScrubShotSettings settings);
}

/// <summary>
/// Versioned JSON settings at <c>%LOCALAPPDATA%\ScrubShot\settings.json</c>.
/// </summary>
/// <remarks>
/// Saves write a temporary file in the same folder and move it over the target, so a crash mid-write leaves
/// the previous file intact. Load never creates anything. A file that is not valid JSON is set aside under a
/// timestamped name and defaults are used; a file that is valid JSON but has bad leaves keeps every good
/// leaf (see <see cref="SettingsDocument.Normalize"/>). A file that could not be read, or a corrupt file that
/// could not be set aside, is set aside before the next save rather than overwritten; if that fails the save
/// throws. A file written by a newer schema is copied to <c>settings.v{N}.json</c> before this build first
/// rewrites it.
/// <para>
/// A save writes differences, not the whole document (D-048): the store keeps the document as it was found in
/// the file and replaces only the settings whose value differs from what was loaded. A setting the file never
/// mentioned stays unmentioned, so a default that changes in a later version still reaches this user; a value
/// the file spelled in a way this version resets, and properties this version does not know, stay as written.
/// Comments are not kept. With no usable file the document starts empty and holds only what differs from the
/// defaults.
/// </para>
/// </remarks>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly ILogger _logger;
    private bool _protectExistingFile;
    private int? _newerSchemaVersion;

    // The document as the file had it (before normalization) and the settings that document meant when loaded.
    // Null raw means there is no usable file content to preserve.
    private JsonObject? _raw;
    private ScrubShotSettings? _loaded;

    public JsonSettingsStore(string filePath, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
        _logger = logger ?? NullLogger.Instance;
    }

    public static string DefaultFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScrubShot", "settings.json");

    public string FilePath { get; }

    private string Directory => Path.GetDirectoryName(FilePath)!;

    public ScrubShotSettings Load()
    {
        _protectExistingFile = false;
        _newerSchemaVersion = null;
        _raw = null;
        _loaded = null;

        if (!File.Exists(FilePath))
        {
            Log.SettingsLoaded(_logger, ScrubShotSettings.CurrentSchemaVersion, usedDefaults: true);
            return ScrubShotSettings.Defaults;
        }

        string text;
        try
        {
            text = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.SettingsLoadFailed(_logger, ex.HResult);
            _protectExistingFile = true;
            return ScrubShotSettings.Defaults;
        }

        JsonObject root;
        try
        {
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            root = JsonNode.Parse(text, documentOptions: options) as JsonObject ?? throw new JsonException("The settings document is not a JSON object.");
        }
        catch (JsonException ex)
        {
            Log.SettingsLoadFailed(_logger, ex.HResult);
            _protectExistingFile = !TrySetAside(FilePath);
            return ScrubShotSettings.Defaults;
        }

        var raw = (JsonObject)root.DeepClone();
        var reset = SettingsDocument.Normalize(root);
        if (reset > 0)
        {
            Log.SettingsValuesReset(_logger, reset);
        }

        var loadedVersion = root[SettingsDocument.SchemaVersionProperty]!.GetValue<int>();
        if (loadedVersion > ScrubShotSettings.CurrentSchemaVersion)
        {
            _newerSchemaVersion = loadedVersion;
        }

        ScrubShotSettings loaded;
        try
        {
            loaded = root.Deserialize<ScrubShotSettings>(SettingsJson.Options) ?? ScrubShotSettings.Defaults;
        }
        catch (JsonException ex)
        {
            // Normalize guarantees the shape, so this is unexpected; treat it like a corrupt file.
            Log.SettingsLoadFailed(_logger, ex.HResult);
            _protectExistingFile = !TrySetAside(FilePath);
            return ScrubShotSettings.Defaults;
        }

        var migrated = Migrate(loaded);
        _raw = raw;
        _loaded = migrated;
        Log.SettingsLoaded(_logger, migrated.SchemaVersion, usedDefaults: false);
        return migrated;
    }

    public void Save(ScrubShotSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var tempPath = Path.Combine(Directory, $"settings.{Guid.NewGuid():N}.tmp");
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            PreserveExistingFileIfNeeded();
            var document = BuildDocument(settings);

            using (var stream = File.Open(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    document.WriteTo(writer, SettingsJson.Options);
                }

                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, FilePath, overwrite: true);
            _raw = document;
            _loaded = settings with { SchemaVersion = ScrubShotSettings.CurrentSchemaVersion };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.SettingsSaveFailed(_logger, ex.HResult);
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// The document to write: the file's own content with only the settings that differ from what was loaded
    /// replaced. Names are matched without regard to case, as loading does, so "output" in a hand-written file
    /// does not gain a second "Output".
    /// </summary>
    private JsonObject BuildDocument(ScrubShotSettings settings)
    {
        var document = _raw is null ? new JsonObject() : (JsonObject)_raw.DeepClone();
        var baseline = ToNode(_loaded ?? ScrubShotSettings.Defaults);
        var wanted = ToNode(settings);

        SetProperty(document, SettingsDocument.SchemaVersionProperty, JsonValue.Create(ScrubShotSettings.CurrentSchemaVersion));
        foreach (var definition in SettingsSchema.All)
        {
            var (section, name) = SettingsDocument.Split(definition.Path);
            var value = wanted[section]![name];
            if (JsonNode.DeepEquals(value, baseline[section]![name]))
            {
                continue; // unchanged: whatever the file says about it, or does not say, stays
            }

            var sectionName = FindProperty(document, section) ?? section;
            if (document[sectionName] is not JsonObject sectionObject)
            {
                sectionObject = [];
                document[sectionName] = sectionObject;
            }

            SetProperty(sectionObject, name, value?.DeepClone());
        }

        return document;
    }

    private static JsonObject ToNode(ScrubShotSettings settings) =>
        JsonSerializer.SerializeToNode(settings, SettingsJson.Options)?.AsObject()
        ?? throw new InvalidOperationException("Settings serialized to null.");

    private static string? FindProperty(JsonObject target, string name) =>
        target.Select(property => property.Key).FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));

    private static void SetProperty(JsonObject target, string name, JsonNode? value) =>
        target[FindProperty(target, name) ?? name] = value;

    /// <summary>Upgrades older documents. Schema 1 is the first; newer documents are read as-is (unknown properties ignored).</summary>
    private static ScrubShotSettings Migrate(ScrubShotSettings loaded) =>
        loaded.SchemaVersion < ScrubShotSettings.CurrentSchemaVersion
            ? loaded with { SchemaVersion = ScrubShotSettings.CurrentSchemaVersion }
            : loaded;

    private void PreserveExistingFileIfNeeded()
    {
        if (!File.Exists(FilePath))
        {
            _protectExistingFile = false;
            _newerSchemaVersion = null;
            return;
        }

        if (_protectExistingFile)
        {
            if (!TrySetAside(FilePath))
            {
                throw new IOException("The existing settings file could not be read earlier and could not be set aside; refusing to overwrite it.");
            }

            _protectExistingFile = false;
        }

        if (_newerSchemaVersion is int newer)
        {
            var backup = Path.Combine(Directory, $"settings.v{newer.ToString(CultureInfo.InvariantCulture)}.json");
            if (!File.Exists(backup))
            {
                File.Copy(FilePath, backup, overwrite: false);
            }

            _newerSchemaVersion = null;
        }
    }

    /// <summary>Moves the file to a timestamped <c>settings.invalid.*.json</c> name without overwriting anything. Best effort.</summary>
    private static bool TrySetAside(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path)!;
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            var target = Path.Combine(directory, $"settings.invalid.{stamp}.json");
            for (var attempt = 2; File.Exists(target); attempt++)
            {
                target = Path.Combine(directory, $"settings.invalid.{stamp}-{attempt.ToString(CultureInfo.InvariantCulture)}.json");
            }

            File.Move(path, target, overwrite: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing to do; a stray temp file is harmless.
        }
    }
}
