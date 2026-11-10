// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Reflection;

namespace ScrubShot.Core.Settings;

/// <summary>One leaf setting: its dotted path, its CLR type and its default value.</summary>
public sealed record SettingDefinition(string Path, Type ValueType, object? DefaultValue);

/// <summary>
/// The list of every leaf setting, derived from the <see cref="ScrubShotSettings"/> record shape so the
/// policy resolver, the Settings UI and the ADMX templates (Milestone 5) share one source of truth.
/// </summary>
public static class SettingsSchema
{
    private static readonly Lazy<IReadOnlyList<SettingDefinition>> Definitions = new(Build);

    private static readonly Lazy<IReadOnlyDictionary<string, SettingDefinition>> ByPath = new(() =>
        Definitions.Value.ToDictionary(d => d.Path, StringComparer.OrdinalIgnoreCase));

    /// <summary>All settings in declaration order. Excludes <c>SchemaVersion</c>, which is not a setting.</summary>
    public static IReadOnlyList<SettingDefinition> All => Definitions.Value;

    public static bool TryGet(string path, out SettingDefinition definition) => ByPath.Value.TryGetValue(path, out definition!);

    private static List<SettingDefinition> Build()
    {
        var defaults = ScrubShotSettings.Defaults;
        var list = new List<SettingDefinition>();
        foreach (var section in typeof(ScrubShotSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (section.Name == nameof(ScrubShotSettings.SchemaVersion))
            {
                continue;
            }

            var sectionValue = section.GetValue(defaults);
            foreach (var leaf in section.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!IsLeafType(leaf.PropertyType))
                {
                    throw new InvalidOperationException($"Setting {section.Name}.{leaf.Name} has unsupported type {leaf.PropertyType.Name}.");
                }

                list.Add(new SettingDefinition($"{section.Name}.{leaf.Name}", leaf.PropertyType, leaf.GetValue(sectionValue)));
            }
        }

        return list;
    }

    private static bool IsLeafType(Type type) =>
        type == typeof(bool) || type == typeof(int) || type == typeof(string) || type.IsEnum;
}
