// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Globalization;
using System.Text.RegularExpressions;

namespace ScrubShot.Core.Output;

/// <summary>
/// Expands a file name pattern into a safe Windows file name (without extension).
/// </summary>
/// <remarks>
/// Tokens: <c>${YYYY}</c>, <c>${MM}</c>, <c>${DD}</c>, <c>${hh}</c> (24-hour), <c>${mm}</c>, <c>${ss}</c>,
/// <c>${NUM}</c> (sequence number, four digits) and <c>${title}</c> (reserved; the active window title,
/// empty until window capture supplies it). Tokens are case-sensitive. Unknown tokens stay as written.
/// The result is sanitized: invalid characters become underscores, trailing dots and spaces are removed,
/// reserved device names are prefixed, and the length is capped.
/// </remarks>
public static partial class FileNamePattern
{
    public const string DefaultPattern = "${YYYY}-${MM}-${DD}_${hh}-${mm}-${ss}";

    public const string FallbackName = "capture";

    public const int MaxLength = 120;

    // Windows device names. A reserved name followed by an extension ("NUL.txt") still names the device, so the
    // check runs on the text before the first dot. The superscript digits are reserved as well.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    public static string Expand(string pattern, DateTimeOffset timestamp, int sequenceNumber = 1, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var expanded = TokenRegex().Replace(pattern, match => match.Groups[1].Value switch
        {
            "YYYY" => timestamp.ToString("yyyy", CultureInfo.InvariantCulture),
            "MM" => timestamp.ToString("MM", CultureInfo.InvariantCulture),
            "DD" => timestamp.ToString("dd", CultureInfo.InvariantCulture),
            "hh" => timestamp.ToString("HH", CultureInfo.InvariantCulture),
            "mm" => timestamp.ToString("mm", CultureInfo.InvariantCulture),
            "ss" => timestamp.ToString("ss", CultureInfo.InvariantCulture),
            "NUM" => sequenceNumber.ToString("0000", CultureInfo.InvariantCulture),
            "title" => title ?? string.Empty,
            _ => match.Value,
        });

        return Sanitize(expanded);
    }

    /// <summary>Makes any string a valid Windows file name without an extension.</summary>
    public static string Sanitize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0 || char.IsControl(chars[i]))
            {
                chars[i] = '_';
            }
        }

        var result = new string(chars).Trim().TrimEnd('.', ' ');
        if (result.Length > MaxLength)
        {
            // Never cut between the halves of a surrogate pair.
            var cut = char.IsHighSurrogate(result[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
            result = result[..cut].TrimEnd('.', ' ');
        }

        if (result.Length == 0)
        {
            return FallbackName;
        }

        var dot = result.IndexOf('.', StringComparison.Ordinal);
        var baseName = (dot < 0 ? result : result[..dot]).TrimEnd(' ');
        return ReservedNames.Contains(baseName) ? "_" + result : result;
    }

    [GeneratedRegex(@"\$\{([A-Za-z]+)\}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TokenRegex();
}
