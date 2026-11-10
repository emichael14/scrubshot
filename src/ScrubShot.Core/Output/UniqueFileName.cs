// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Globalization;

namespace ScrubShot.Core.Output;

/// <summary>Resolves file name collisions by appending <c>-2</c>, <c>-3</c>, ... before the extension.</summary>
public static class UniqueFileName
{
    public const int MaxAttempts = 10_000;

    /// <summary>Returns the full path of <paramref name="fileName"/> in <paramref name="directory"/>, or a numbered variant if it already exists.</summary>
    public static string Resolve(string directory, string fileName) => Resolve(directory, fileName, File.Exists);

    /// <summary>Same as <see cref="Resolve(string, string)"/> with an injectable existence check for tests.</summary>
    public static string Resolve(string directory, string fileName, Func<string, bool> exists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(exists);

        var candidate = Path.Combine(directory, fileName);
        if (!exists(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var n = 2; n <= MaxAttempts; n++)
        {
            candidate = Path.Combine(directory, $"{stem}-{n.ToString(CultureInfo.InvariantCulture)}{extension}");
            if (!exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Could not find a free file name after {MaxAttempts} attempts.");
    }
}
