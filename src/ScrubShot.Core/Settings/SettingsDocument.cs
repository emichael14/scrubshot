// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScrubShot.Core.Settings;

/// <summary>
/// Shapes a settings JSON document to the schema before it is bound: section and property names are
/// canonicalized (case-insensitive match), missing or malformed sections become default sections, and
/// every leaf whose value is absent, null, of the wrong JSON kind or an undefined enum name is reset to
/// its default. One bad leaf therefore never discards the rest of the file.
/// </summary>
public static class SettingsDocument
{
    public const string SchemaVersionProperty = nameof(ScrubShotSettings.SchemaVersion);

    private static readonly Lazy<JsonObject> DefaultsRoot = new(() =>
        JsonSerializer.SerializeToNode(ScrubShotSettings.Defaults, SettingsJson.Options)?.AsObject()
        ?? throw new InvalidOperationException("Defaults serialized to null."));

    private static readonly Lazy<IReadOnlyList<string>> SectionNames = new(() =>
        SettingsSchema.All.Select(d => d.Path[..d.Path.IndexOf('.', StringComparison.Ordinal)]).Distinct(StringComparer.Ordinal).ToList());

    /// <summary>A fresh JSON document holding the defaults.</summary>
    public static JsonObject CreateDefaults() => (JsonObject)DefaultsRoot.Value.DeepClone();

    /// <summary>
    /// Normalizes <paramref name="root"/> in place. Returns the number of sections and leaves that were present
    /// but invalid and were reset; absent ones are filled with defaults silently, since a sparse file is legitimate.
    /// </summary>
    public static int Normalize(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var reset = 0;

        Canonicalize(root, SchemaVersionProperty);
        if (root[SchemaVersionProperty] is not JsonValue version || version.GetValueKind() != JsonValueKind.Number || !version.TryGetValue<int>(out _))
        {
            root[SchemaVersionProperty] = ScrubShotSettings.CurrentSchemaVersion;
        }

        foreach (var section in SectionNames.Value)
        {
            Canonicalize(root, section);
            if (root[section] is not JsonObject)
            {
                if (root.ContainsKey(section))
                {
                    reset++;
                }

                root[section] = DefaultsRoot.Value[section]!.DeepClone();
            }
        }

        foreach (var definition in SettingsSchema.All)
        {
            var (section, name) = Split(definition.Path);
            var sectionObject = root[section]!.AsObject();
            Canonicalize(sectionObject, name);
            var node = sectionObject[name];
            if (TryCanonicalValue(definition, node, out var canonical))
            {
                if (!ReferenceEquals(canonical, node))
                {
                    sectionObject[name] = canonical;
                }
            }
            else
            {
                if (sectionObject.ContainsKey(name))
                {
                    reset++;
                }

                sectionObject[name] = DefaultsRoot.Value[section]![name]!.DeepClone();
            }
        }

        return reset;
    }

    /// <summary>Returns the node to keep for a leaf: the node itself when valid, a canonical replacement (enum name casing), or false when it must be reset.</summary>
    private static bool TryCanonicalValue(SettingDefinition definition, JsonNode? node, out JsonNode canonical)
    {
        canonical = node!;
        if (node is not JsonValue value)
        {
            return false;
        }

        var kind = value.GetValueKind();
        var type = definition.ValueType;
        if (type == typeof(bool))
        {
            return kind is JsonValueKind.True or JsonValueKind.False;
        }

        if (type == typeof(int))
        {
            return kind == JsonValueKind.Number && value.TryGetValue<int>(out _);
        }

        if (type == typeof(string))
        {
            return kind == JsonValueKind.String;
        }

        if (type.IsEnum)
        {
            if (kind != JsonValueKind.String || !value.TryGetValue<string>(out var text))
            {
                return false;
            }

            var name = MatchEnumName(type, text);
            if (name is null)
            {
                return false;
            }

            if (!string.Equals(name, text, StringComparison.Ordinal))
            {
                canonical = JsonValue.Create(name);
            }

            return true;
        }

        return false;
    }

    /// <summary>Exact member-name match, case-insensitive. Rejects numbers and comma lists, which <see cref="Enum.TryParse(Type, string, out object)"/> would accept.</summary>
    internal static string? MatchEnumName(Type enumType, string text)
    {
        var trimmed = text.Trim();
        return Enum.GetNames(enumType).FirstOrDefault(n => n.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Renames a property whose name matches <paramref name="canonicalName"/> except for case.</summary>
    private static void Canonicalize(JsonObject parent, string canonicalName)
    {
        if (parent.ContainsKey(canonicalName))
        {
            return;
        }

        var actual = parent.Select(p => p.Key).FirstOrDefault(k => k.Equals(canonicalName, StringComparison.OrdinalIgnoreCase));
        if (actual is null)
        {
            return;
        }

        var node = parent[actual];
        parent.Remove(actual);
        parent[canonicalName] = node;
    }

    internal static (string Section, string Name) Split(string path)
    {
        var dot = path.IndexOf('.', StringComparison.Ordinal);
        return (path[..dot], path[(dot + 1)..]);
    }
}
