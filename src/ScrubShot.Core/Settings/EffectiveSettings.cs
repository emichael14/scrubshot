// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

namespace ScrubShot.Core.Settings;

/// <summary>Where a setting's effective value came from, lowest precedence first.</summary>
public enum SettingSource
{
    Default,
    User,
    UserPolicy,
    MachinePolicy,
}

/// <summary>
/// The settings after policy has been applied, plus the source of every leaf so the UI can show a lock.
/// </summary>
public sealed class EffectiveSettings
{
    private readonly IReadOnlyDictionary<string, SettingSource> _sources;

    public EffectiveSettings(ScrubShotSettings values, IReadOnlyDictionary<string, SettingSource> sources)
    {
        Values = values;
        _sources = sources;
    }

    public ScrubShotSettings Values { get; }

    public IEnumerable<string> Paths => _sources.Keys;

    /// <summary>Source of the setting at <paramref name="path"/> (for example "Output.DefaultAction").</summary>
    public SettingSource SourceOf(string path) =>
        _sources.TryGetValue(path, out var source) ? source : throw new ArgumentException($"Unknown setting path '{path}'.", nameof(path));

    /// <summary>True when policy set the value; the UI renders it read-only with a lock.</summary>
    public bool IsLocked(string path) => SourceOf(path) >= SettingSource.UserPolicy;

    public IReadOnlyDictionary<string, SettingSource> Sources => _sources;
}
