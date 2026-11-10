// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScrubShot.Core.Logging;

namespace ScrubShot.Core.Settings;

/// <summary>
/// Applies the precedence machine policy &gt; user policy &gt; user settings &gt; defaults and reports which
/// one won for every setting. Policy values that name an unknown setting or cannot be converted to the
/// setting's type are ignored, so a typo in a policy never breaks the app or unlocks anything.
/// </summary>
public static class SettingsResolver
{
    public static EffectiveSettings Resolve(
        ScrubShotSettings userSettings,
        IPolicySource machinePolicy,
        IPolicySource userPolicy,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(userSettings);
        ArgumentNullException.ThrowIfNull(machinePolicy);
        ArgumentNullException.ThrowIfNull(userPolicy);
        logger ??= NullLogger.Instance;

        var defaults = SettingsDocument.CreateDefaults();
        var merged = JsonSerializer.SerializeToNode(userSettings, SettingsJson.Options)?.AsObject()
            ?? throw new InvalidOperationException("Settings serialized to null.");
        var reset = SettingsDocument.Normalize(merged);
        if (reset > 0)
        {
            Log.SettingsValuesReset(logger, reset);
        }

        var sources = new Dictionary<string, SettingSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in SettingsSchema.All)
        {
            var (section, name) = SettingsDocument.Split(definition.Path);
            var userNode = merged[section]![name];
            var defaultNode = defaults[section]![name];
            sources[definition.Path] = JsonNode.DeepEquals(userNode, defaultNode) ? SettingSource.Default : SettingSource.User;
        }

        var ignored = 0;
        ignored += Apply(merged, sources, userPolicy.Read(), SettingSource.UserPolicy);
        ignored += Apply(merged, sources, machinePolicy.Read(), SettingSource.MachinePolicy);
        if (ignored > 0)
        {
            Log.PolicyValuesIgnored(logger, ignored);
        }

        var values = merged.Deserialize<ScrubShotSettings>(SettingsJson.Options) ?? ScrubShotSettings.Defaults;
        return new EffectiveSettings(values, sources);
    }

    private static int Apply(JsonObject merged, Dictionary<string, SettingSource> sources, IReadOnlyDictionary<string, object> policy, SettingSource source)
    {
        var ignored = 0;
        foreach (var (name, raw) in policy)
        {
            if (!SettingsSchema.TryGet(name, out var definition) || !TryConvert(definition.ValueType, raw, out var node))
            {
                ignored++;
                continue;
            }

            var (section, leaf) = SettingsDocument.Split(definition.Path);
            merged[section]!.AsObject()[leaf] = node;
            sources[definition.Path] = source;
        }

        return ignored;
    }

    /// <summary>Converts a registry-shaped value (int or string) to a JSON node of the setting's type.</summary>
    internal static bool TryConvert(Type type, object raw, out JsonNode node)
    {
        node = null!;
        if (type == typeof(bool))
        {
            switch (raw)
            {
                case int i:
                    node = JsonValue.Create(i != 0);
                    return true;
                case string s when bool.TryParse(s, out var b):
                    node = JsonValue.Create(b);
                    return true;
                case string s when s is "1" or "0":
                    node = JsonValue.Create(s == "1");
                    return true;
                default:
                    return false;
            }
        }

        if (type == typeof(int))
        {
            switch (raw)
            {
                case int i:
                    node = JsonValue.Create(i);
                    return true;
                case string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i):
                    node = JsonValue.Create(i);
                    return true;
                default:
                    return false;
            }
        }

        if (type.IsEnum)
        {
            switch (raw)
            {
                case string s when SettingsDocument.MatchEnumName(type, s) is { } name:
                    node = JsonValue.Create(name);
                    return true;
                case int i when Enum.GetUnderlyingType(type) == typeof(int) && Enum.IsDefined(type, i):
                    node = JsonValue.Create(Enum.GetName(type, i)!);
                    return true;
                default:
                    return false;
            }
        }

        if (type == typeof(string) && raw is string text)
        {
            node = JsonValue.Create(text);
            return true;
        }

        return false;
    }
}
