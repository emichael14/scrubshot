// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

namespace ScrubShot.Output;

/// <summary>
/// The notification sentence for an <see cref="OutputResult"/>. It names the action and the folder, never the file
/// name or anything about the capture's content, and it always names an output that failed, so a capture cannot
/// fail silently.
/// </summary>
public static class OutputMessages
{
    public static string For(OutputResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var folder = result.SavedPath is { } path ? Path.GetDirectoryName(path) : null;
        return (result.Copied, folder, result.CopyFailed, result.SaveFailed) switch
        {
            (true, { }, _, _) => $"Copied to the clipboard and saved to {folder}",
            (true, null, _, true) => "Copied to the clipboard. Saving the file failed; check the save folder in Settings.",
            (true, null, _, false) => "Copied to the clipboard",
            (false, { }, true, _) => $"Saved to {folder}. Copying to the clipboard failed.",
            (false, { }, false, _) => $"Saved to {folder}",
            (false, null, true, true) => "The capture could not be copied or saved.",
            (false, null, true, false) => "Copying to the clipboard failed.",
            (false, null, false, true) => "Saving the file failed; check the save folder in Settings.",
            _ => "Capture finished",
        };
    }
}
