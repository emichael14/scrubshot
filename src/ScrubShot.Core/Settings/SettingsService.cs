// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScrubShot.Core.Logging;

namespace ScrubShot.Core.Settings;

/// <summary>What became of a proposed settings change.</summary>
public enum SettingsChangeOutcome
{
    /// <summary>Written to the user's file and in effect.</summary>
    Saved,

    /// <summary>The proposal did not differ from the current values in any setting the user may change.</summary>
    NoChange,

    /// <summary>A changed value is not valid; nothing was written. See <see cref="SettingsChangeResult.Issues"/>.</summary>
    Invalid,

    /// <summary>The settings file could not be written; nothing changed.</summary>
    SaveFailed,
}

public sealed record SettingsChangeResult(SettingsChangeOutcome Outcome, IReadOnlyList<SettingIssue> Issues)
{
    public bool Saved => Outcome == SettingsChangeOutcome.Saved;
}

/// <summary>
/// The one place settings are loaded, changed and saved at run time (D-045, D-048). It holds the user's own document
/// and the effective settings after policy, and applies a proposed change under three rules: a setting locked by
/// policy cannot be changed and the user's own stored value for it is left untouched; a changed value must be valid
/// or nothing is written; only the settings that actually changed are handed to the store as changes, so every
/// other stored value stays as it was.
/// </summary>
/// <remarks>
/// Policy is read again on every load, before and after every change, and whenever <see cref="RefreshPolicy"/> is
/// called (the app does that at the start of each capture). A policy read that fails never unlocks anything: the
/// last complete read of that hive stays in force until a read succeeds again.
/// </remarks>
public sealed class SettingsService
{
    /// <summary>How often the very first policy read is retried before the app starts without it. Later failures keep the last good read instead.</summary>
    public const int FirstReadAttempts = 3;

    private static readonly TimeSpan FirstReadRetryDelay = TimeSpan.FromMilliseconds(40);
    private static readonly IReadOnlyDictionary<string, object> NoPolicy = new Dictionary<string, object>();

    private readonly ISettingsStore _store;
    private readonly IPolicySource _machinePolicy;
    private readonly IPolicySource _userPolicy;
    private readonly Func<string?> _picturesFolder;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private ScrubShotSettings _userSettings = ScrubShotSettings.Defaults;
    private IReadOnlyDictionary<string, object>? _machineLastGood;
    private IReadOnlyDictionary<string, object>? _userLastGood;
    private volatile EffectiveSettings _effective;

    public SettingsService(
        ISettingsStore store,
        IPolicySource machinePolicy,
        IPolicySource userPolicy,
        Func<string?> picturesFolder,
        ILogger? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _machinePolicy = machinePolicy ?? throw new ArgumentNullException(nameof(machinePolicy));
        _userPolicy = userPolicy ?? throw new ArgumentNullException(nameof(userPolicy));
        _picturesFolder = picturesFolder ?? throw new ArgumentNullException(nameof(picturesFolder));
        _logger = logger ?? NullLogger.Instance;
        _effective = SettingsResolver.Resolve(ScrubShotSettings.Defaults, InMemoryPolicySource.Empty, InMemoryPolicySource.Empty);
    }

    /// <summary>
    /// Raised whenever the effective settings or their locks changed: after a saved change, and after a reload or
    /// policy refresh that found something different. It runs on whichever thread made that call, which for a
    /// policy refresh may be a background thread; handlers that touch UI must marshal.
    /// </summary>
    public event Action<EffectiveSettings>? Changed;

    /// <summary>The settings in effect: defaults, the user's file, then policy. Safe to read from any thread.</summary>
    public EffectiveSettings Effective => _effective;

    /// <summary>
    /// Reads the user's file and policy again. Nothing in a settings file or a policy key may stop the app: on a
    /// failure the previous effective settings (defaults at first) stay in force.
    /// </summary>
    public EffectiveSettings Reload() => Update(reloadFile: true);

    /// <summary>
    /// Reads policy again and applies it to the user's settings already in memory: two small registry reads, no
    /// file access. Cheap enough to call before every capture, so a policy pushed while the app runs is enforced
    /// from the next capture on.
    /// </summary>
    public EffectiveSettings RefreshPolicy() => Update(reloadFile: false);

    /// <summary>Problems with the values currently in effect, for display. A locked setting can be listed; the user cannot fix it.</summary>
    public IReadOnlyList<SettingIssue> ValidateCurrent() => SettingsValidator.Validate(_effective.Values, _picturesFolder());

    /// <summary>
    /// Applies the differences between the effective values and <paramref name="proposed"/>. Edits to locked settings
    /// are dropped. Only problems with settings that changed can refuse the change, so an old bad value elsewhere in
    /// the file never blocks an unrelated edit.
    /// </summary>
    public SettingsChangeResult TryChange(ScrubShotSettings proposed)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        EffectiveSettings before;
        EffectiveSettings after;
        SettingsChangeResult result;
        lock (_gate)
        {
            before = _effective;
            result = TryChangeLocked(proposed);
            after = _effective;
        }

        if (!Same(before, after))
        {
            Changed?.Invoke(after);
        }

        return result;
    }

    private EffectiveSettings Update(bool reloadFile)
    {
        EffectiveSettings before;
        EffectiveSettings after;
        lock (_gate)
        {
            before = _effective;
            try
            {
                var user = reloadFile ? _store.Load() : _userSettings;
                var resolved = Resolve(user);
                _userSettings = user;

                // Keep the same object when nothing changed, so readers that hold it are not disturbed.
                after = Same(before, resolved) ? before : resolved;
                _effective = after;
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException)
            {
                Log.SettingsLoadFailed(_logger, ErrorCodes.HResultOf(ex));
                return before;
            }
        }

        if (!ReferenceEquals(before, after))
        {
            Changed?.Invoke(after);
        }

        return after;
    }

    private SettingsChangeResult TryChangeLocked(ScrubShotSettings proposed)
    {
        // Start from the policy as it is now, so a lock that arrived since the last read already holds for this change.
        EffectiveSettings effective;
        try
        {
            var resolved = Resolve(_userSettings);
            effective = Same(_effective, resolved) ? _effective : resolved;
            _effective = effective;
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            Log.SettingsLoadFailed(_logger, ErrorCodes.HResultOf(ex));
            effective = _effective;
        }

        var currentNode = ToNode(effective.Values);
        var proposedNode = ToNode(proposed);
        SettingsDocument.Normalize(proposedNode);

        var changed = new List<string>();
        foreach (var definition in SettingsSchema.All)
        {
            var (section, name) = SettingsDocument.Split(definition.Path);
            if (!effective.IsLocked(definition.Path) && !JsonNode.DeepEquals(currentNode[section]![name], proposedNode[section]![name]))
            {
                changed.Add(definition.Path);
            }
        }

        if (changed.Count == 0)
        {
            return new SettingsChangeResult(SettingsChangeOutcome.NoChange, []);
        }

        // Validate what would actually take effect: the proposal with every locked setting at its policy value.
        var candidateNode = (JsonObject)currentNode.DeepClone();
        Copy(proposedNode, candidateNode, changed);
        var candidate = FromNode(candidateNode);
        var blocking = SettingsValidator.Validate(candidate, _picturesFolder())
            .Where(issue => changed.Contains(issue.Path, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (blocking.Count > 0)
        {
            return new SettingsChangeResult(SettingsChangeOutcome.Invalid, blocking);
        }

        // The user's own document with only the changed values replaced. The value the user had stored for a locked
        // setting survives, and comes back if the policy is ever lifted. The store writes just these differences.
        var userNode = ToNode(_userSettings);
        Copy(proposedNode, userNode, changed);
        var toSave = FromNode(userNode);
        try
        {
            _store.Save(toSave);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SettingsChangeResult(SettingsChangeOutcome.SaveFailed, []);
        }

        _userSettings = toSave;
        try
        {
            _effective = Resolve(toSave);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            // The file is written but could not be resolved again. Keep the saved values with the locks known so far.
            Log.SettingsLoadFailed(_logger, ErrorCodes.HResultOf(ex));
            _effective = new EffectiveSettings(candidate, effective.Sources);
        }

        return new SettingsChangeResult(SettingsChangeOutcome.Saved, []);
    }

    /// <summary>Resolves <paramref name="user"/> under the policy as it can be read now, failing closed per hive.</summary>
    private EffectiveSettings Resolve(ScrubShotSettings user)
    {
        var machine = ReadPolicy(_machinePolicy, ref _machineLastGood);
        var userPolicy = ReadPolicy(_userPolicy, ref _userLastGood);
        return SettingsResolver.Resolve(user, new InMemoryPolicySource(machine), new InMemoryPolicySource(userPolicy), _logger);
    }

    private IReadOnlyDictionary<string, object> ReadPolicy(IPolicySource source, ref IReadOnlyDictionary<string, object>? lastGood)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var values = source.Read();
                lastGood = values;
                return values;
            }
            catch (PolicyReadException ex)
            {
                Log.PolicyReadFailed(_logger, ErrorCodes.HResultOf(ex));
                if (lastGood is not null)
                {
                    return lastGood; // fail closed: what was locked stays locked until a read succeeds
                }

                if (attempt >= FirstReadAttempts)
                {
                    // Nothing is known about this hive yet. Run on defaults and the user's file; the next refresh
                    // (the next capture, the next time Settings opens) tries again.
                    return NoPolicy;
                }

                Thread.Sleep(FirstReadRetryDelay);
            }
        }
    }

    private static bool Same(EffectiveSettings a, EffectiveSettings b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Values != b.Values || a.Sources.Count != b.Sources.Count)
        {
            return false;
        }

        foreach (var (path, source) in a.Sources)
        {
            if (!b.Sources.TryGetValue(path, out var other) || other != source)
            {
                return false;
            }
        }

        return true;
    }

    private static JsonObject ToNode(ScrubShotSettings settings) =>
        JsonSerializer.SerializeToNode(settings, SettingsJson.Options)?.AsObject()
        ?? throw new InvalidOperationException("Settings serialized to null.");

    private static ScrubShotSettings FromNode(JsonObject node) =>
        node.Deserialize<ScrubShotSettings>(SettingsJson.Options) ?? throw new InvalidOperationException("Settings deserialized to null.");

    private static void Copy(JsonObject from, JsonObject to, IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var (section, name) = SettingsDocument.Split(path);
            to[section]![name] = from[section]![name]?.DeepClone();
        }
    }
}
