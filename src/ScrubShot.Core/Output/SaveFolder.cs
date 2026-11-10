// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Text.RegularExpressions;

namespace ScrubShot.Core.Output;

/// <summary>Why a configured save folder cannot be used. The Settings window shows these; a save fails with them.</summary>
public enum SaveFolderProblem
{
    None,

    /// <summary>Rooted but not absolute (<c>\Shots</c>, <c>D:Shots</c>): Windows would resolve it against the current drive or directory.</summary>
    NotFullyQualified,

    /// <summary>A relative or empty value needs the Pictures folder, and Windows did not report one.</summary>
    PicturesUnavailable,

    /// <summary>An environment variable in the value is not defined.</summary>
    UnresolvedVariable,

    /// <summary>Not a valid Windows path.</summary>
    Invalid,
}

/// <summary>
/// Turns the <c>Output.SaveFolder</c> setting into an absolute folder (D-043): environment variables are expanded;
/// an absolute path is used as is; a relative value lands under the user's Pictures folder; empty means
/// Pictures\ScrubShot. The result never depends on the process's current directory. Pure apart from reading
/// environment variables, so the saver and the Settings window share it.
/// </summary>
public static partial class SaveFolder
{
    public const string DefaultSubfolder = "ScrubShot";

    public static bool TryResolve(string? configuredFolder, string? picturesFolder, out string folder, out SaveFolderProblem problem)
    {
        folder = string.Empty;
        problem = SaveFolderProblem.None;
        var expanded = Environment.ExpandEnvironmentVariables(configuredFolder ?? string.Empty).Trim();
        if (UnexpandedVariable().IsMatch(expanded))
        {
            problem = SaveFolderProblem.UnresolvedVariable;
            return false;
        }

        try
        {
            if (expanded.Length > 0 && Path.IsPathFullyQualified(expanded))
            {
                folder = Path.GetFullPath(expanded);
                return true;
            }

            if (expanded.Length > 0 && Path.IsPathRooted(expanded))
            {
                problem = SaveFolderProblem.NotFullyQualified;
                return false;
            }

            if (string.IsNullOrWhiteSpace(picturesFolder) || !Path.IsPathFullyQualified(picturesFolder))
            {
                problem = SaveFolderProblem.PicturesUnavailable;
                return false;
            }

            folder = Path.GetFullPath(Path.Combine(picturesFolder, expanded.Length == 0 ? DefaultSubfolder : expanded));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            problem = SaveFolderProblem.Invalid;
            return false;
        }
    }

    /// <summary>The folder a settings value resolves to. Throws an <see cref="IOException"/> whose message never contains the value.</summary>
    public static string Resolve(string? configuredFolder, string? picturesFolder) =>
        TryResolve(configuredFolder, picturesFolder, out var folder, out var problem)
            ? folder
            : throw new IOException(Describe(problem));

    /// <summary>One fixed sentence per problem. Never contains the configured value, so it is safe to log or show.</summary>
    public static string Describe(SaveFolderProblem problem) => problem switch
    {
        SaveFolderProblem.None => string.Empty,
        SaveFolderProblem.NotFullyQualified => "The save folder must be an absolute path, or a folder name relative to Pictures.",
        SaveFolderProblem.PicturesUnavailable => "The Pictures folder is unavailable; set an absolute save folder.",
        SaveFolderProblem.UnresolvedVariable => "The save folder uses an environment variable that is not defined.",
        _ => "The save folder is not a valid path.",
    };

    /// <summary>The user's Pictures folder even when it does not exist yet (a redirected folder), or empty when Windows reports none.</summary>
    public static string PicturesFolder() =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures, Environment.SpecialFolderOption.DoNotVerify);

    [GeneratedRegex(@"%[^%\\/:*?""<>|]+%")]
    private static partial Regex UnexpandedVariable();
}
