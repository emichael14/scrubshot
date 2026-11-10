// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Hotkeys;
using ScrubShot.Core.Output;

namespace ScrubShot.Core.Settings;

/// <summary>What is wrong with one setting's value. The Settings window turns these into sentences.</summary>
public enum SettingProblem
{
    HotkeyInvalid,
    HotkeyReservedKey,
    HotkeyNeedsModifier,
    HotkeyDuplicate,
    SaveFolderNotFullyQualified,
    SaveFolderPicturesUnavailable,
    SaveFolderUnresolvedVariable,
    SaveFolderInvalid,
    JpgQualityOutOfRange,
    DelayOutOfRange,
}

/// <summary>A problem with the setting at <paramref name="Path"/> ("Section.Name").</summary>
public sealed record SettingIssue(string Path, SettingProblem Problem);

/// <summary>
/// Checks the values a user can type: hotkey gestures, the save folder and the numeric ranges. Pure; no registry,
/// no disk. A duplicate hotkey is reported on every action that shares the gesture, so whichever one the user just
/// edited is flagged.
/// </summary>
public static class SettingsValidator
{
    public const int MinJpgQuality = 1;
    public const int MaxJpgQuality = 100;
    public const int MaxDelayMilliseconds = 10_000;

    public static IReadOnlyList<SettingIssue> Validate(ScrubShotSettings settings, string? picturesFolder)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var issues = new List<SettingIssue>();

        var checks = HotkeyBindings.Precheck(settings.Hotkeys);
        foreach (var check in checks)
        {
            SettingProblem? problem = check.Conflict switch
            {
                HotkeyConflict.InvalidGesture => SettingProblem.HotkeyInvalid,
                HotkeyConflict.ReservedKey => SettingProblem.HotkeyReservedKey,
                HotkeyConflict.NeedsModifier => SettingProblem.HotkeyNeedsModifier,
                HotkeyConflict.DuplicateInSettings => SettingProblem.HotkeyDuplicate,
                _ => null,
            };

            // The first holder of a duplicated gesture is valid on its own; flag it too.
            if (problem is null && check.Gesture is not null &&
                checks.Any(other => other.Action != check.Action && other.Conflict == HotkeyConflict.DuplicateInSettings && other.Gesture == check.Gesture))
            {
                problem = SettingProblem.HotkeyDuplicate;
            }

            if (problem is { } found)
            {
                issues.Add(new SettingIssue(HotkeyPath(check.Action), found));
            }
        }

        if (!SaveFolder.TryResolve(settings.Output.SaveFolder, picturesFolder, out _, out var folderProblem))
        {
            issues.Add(new SettingIssue("Output.SaveFolder", folderProblem switch
            {
                SaveFolderProblem.NotFullyQualified => SettingProblem.SaveFolderNotFullyQualified,
                SaveFolderProblem.PicturesUnavailable => SettingProblem.SaveFolderPicturesUnavailable,
                SaveFolderProblem.UnresolvedVariable => SettingProblem.SaveFolderUnresolvedVariable,
                _ => SettingProblem.SaveFolderInvalid,
            }));
        }

        if (settings.Output.JpgQuality is < MinJpgQuality or > MaxJpgQuality)
        {
            issues.Add(new SettingIssue("Output.JpgQuality", SettingProblem.JpgQualityOutOfRange));
        }

        if (settings.Capture.DelayMilliseconds is < 0 or > MaxDelayMilliseconds)
        {
            issues.Add(new SettingIssue("Capture.DelayMilliseconds", SettingProblem.DelayOutOfRange));
        }

        return issues;
    }

    /// <summary>The settings path of an action's hotkey, for example "Hotkeys.Region".</summary>
    public static string HotkeyPath(HotkeyAction action) => $"{nameof(ScrubShotSettings.Hotkeys)}.{action}";
}
