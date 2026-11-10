// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

namespace ScrubShot.Core.Settings;

/// <summary>
/// The editing rules behind the Settings window, kept out of the UI so they can be tested (D-046): each edit is
/// proposed to the settings service at once; a value that is refused stays "pending" with its problem and is not
/// saved; a pending value is tried again when the user commits it again and after any other change is saved,
/// because that change may have removed what was wrong with it (two hotkeys that briefly asked for the same
/// keys); and "reset hotkeys" resets what it can when a default collides with a hotkey that policy fixes.
/// </summary>
public sealed class SettingsEditSession
{
    private const int MaxRounds = 5;

    private static readonly string[] HotkeyPaths = ["Hotkeys.Region", "Hotkeys.Window", "Hotkeys.FullScreen", "Hotkeys.RepeatLastRegion"];

    private readonly Func<EffectiveSettings> _effective;
    private readonly Func<ScrubShotSettings, SettingsChangeResult> _tryChange;
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.OrdinalIgnoreCase);

    public SettingsEditSession(Func<EffectiveSettings> effective, Func<ScrubShotSettings, SettingsChangeResult> tryChange)
    {
        _effective = effective ?? throw new ArgumentNullException(nameof(effective));
        _tryChange = tryChange ?? throw new ArgumentNullException(nameof(tryChange));
    }

    /// <summary>The last edit could not be written to the settings file.</summary>
    public bool SaveFailed { get; private set; }

    /// <summary>The last hotkey reset left at least one hotkey as it was because its default is taken.</summary>
    public bool ResetWasPartial { get; private set; }

    public bool IsLocked(string path) => _effective().IsLocked(path);

    /// <summary>The text the user typed for <paramref name="path"/> that could not be saved, or null when what is saved is what to show.</summary>
    public string? PendingText(string path) => _pending.TryGetValue(path, out var pending) ? pending.Text : null;

    public bool HasPending(string path) => _pending.ContainsKey(path);

    /// <summary>Why the pending value for <paramref name="path"/> was refused, or null when nothing is pending.</summary>
    public SettingProblem? ProblemFor(string path) => _pending.TryGetValue(path, out var pending) ? pending.Problem : null;

    /// <summary>
    /// Proposes a new value for one setting. <paramref name="change"/> produces the new document from the current
    /// one, so it can be applied again later; <paramref name="typedText"/> is what to keep on screen if the value
    /// is refused. Returns what the service said. An edit to a locked setting is dropped.
    /// </summary>
    public SettingsChangeOutcome Change(string path, Func<ScrubShotSettings, ScrubShotSettings> change, string? typedText = null)
    {
        ArgumentNullException.ThrowIfNull(change);
        ClearStatus();
        _pending.Remove(path);
        if (IsLocked(path))
        {
            return SettingsChangeOutcome.NoChange;
        }

        var result = _tryChange(change(_effective().Values));
        switch (result.Outcome)
        {
            case SettingsChangeOutcome.Invalid:
                _pending[path] = new Pending(typedText, ProblemOf(result, path), change);
                break;
            case SettingsChangeOutcome.SaveFailed:
                SaveFailed = true;
                break;
            case SettingsChangeOutcome.Saved:
                RetryPending();
                break;
        }

        return result.Outcome;
    }

    /// <summary>Forgets a pending value: the user put the saved value back.</summary>
    public void Revert(string path)
    {
        ClearStatus();
        _pending.Remove(path);
    }

    /// <summary>
    /// Puts the four hotkeys back to their defaults. A default that is refused (it duplicates a hotkey that keeps
    /// its value, typically one fixed by policy) is left as it is and the rest are still reset.
    /// </summary>
    public SettingsChangeOutcome ResetHotkeys()
    {
        ClearStatus();
        foreach (var path in HotkeyPaths)
        {
            _pending.Remove(path);
        }

        var defaults = new HotkeySettings();
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outcome = SettingsChangeOutcome.Invalid;
        for (var round = 0; round < MaxRounds; round++)
        {
            var current = _effective().Values.Hotkeys;
            var proposal = new HotkeySettings
            {
                Region = keep.Contains(HotkeyPaths[0]) ? current.Region : defaults.Region,
                Window = keep.Contains(HotkeyPaths[1]) ? current.Window : defaults.Window,
                FullScreen = keep.Contains(HotkeyPaths[2]) ? current.FullScreen : defaults.FullScreen,
                RepeatLastRegion = keep.Contains(HotkeyPaths[3]) ? current.RepeatLastRegion : defaults.RepeatLastRegion,
            };

            var result = _tryChange(_effective().Values with { Hotkeys = proposal });
            outcome = result.Outcome;
            if (outcome != SettingsChangeOutcome.Invalid)
            {
                break;
            }

            // Hold back every hotkey the service objected to and try the others again. Each round holds back at
            // least one more, so this ends.
            var heldBackMore = false;
            foreach (var issue in result.Issues)
            {
                heldBackMore |= keep.Add(issue.Path);
            }

            if (!heldBackMore)
            {
                break;
            }
        }

        SaveFailed = outcome == SettingsChangeOutcome.SaveFailed;
        ResetWasPartial = keep.Count > 0 && outcome != SettingsChangeOutcome.SaveFailed;
        if (outcome == SettingsChangeOutcome.Saved)
        {
            RetryPending();
        }

        return outcome;
    }

    /// <summary>The settings changed from outside (a policy refresh, a reload). Pending values for settings that are now locked are dropped.</summary>
    public void OnSettingsChangedElsewhere()
    {
        foreach (var path in _pending.Keys.Where(IsLocked).ToList())
        {
            _pending.Remove(path);
        }
    }

    private static SettingProblem ProblemOf(SettingsChangeResult result, string path) =>
        (result.Issues.FirstOrDefault(issue => string.Equals(issue.Path, path, StringComparison.OrdinalIgnoreCase)) ?? result.Issues[0]).Problem;

    /// <summary>
    /// After a save, tries every pending value again against the new settings. One that now passes is saved (what
    /// is on screen becomes what is in effect); one that still fails keeps its current reason.
    /// </summary>
    private void RetryPending()
    {
        for (var round = 0; round < MaxRounds && _pending.Count > 0; round++)
        {
            var progressed = false;
            foreach (var (path, pending) in _pending.ToList())
            {
                if (IsLocked(path))
                {
                    _pending.Remove(path);
                    continue;
                }

                var result = _tryChange(pending.Change(_effective().Values));
                switch (result.Outcome)
                {
                    case SettingsChangeOutcome.Saved:
                        _pending.Remove(path);
                        progressed = true;
                        break;
                    case SettingsChangeOutcome.NoChange:
                        _pending.Remove(path);
                        break;
                    case SettingsChangeOutcome.Invalid:
                        _pending[path] = pending with { Problem = ProblemOf(result, path) };
                        break;
                    case SettingsChangeOutcome.SaveFailed:
                        SaveFailed = true;
                        return;
                }
            }

            if (!progressed)
            {
                break;
            }
        }
    }

    private void ClearStatus()
    {
        SaveFailed = false;
        ResetWasPartial = false;
    }

    private sealed record Pending(string? Text, SettingProblem Problem, Func<ScrubShotSettings, ScrubShotSettings> Change);
}
