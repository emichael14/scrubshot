// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Text.RegularExpressions;
using ScrubShot.Core.Output;
using ScrubShot.Core.Settings;

namespace ScrubShot.Output;

/// <summary>Writes encoded bytes to a file. The interface exists so the sink can be tested without touching the disk.</summary>
public interface ICaptureSaver
{
    /// <summary>Writes <paramref name="encoded"/> into the configured folder under the configured pattern and returns the full path.</summary>
    Task<string> SaveAsync(ReadOnlyMemory<byte> encoded, OutputSettings settings, DateTimeOffset capturedAt, CancellationToken cancellationToken);
}

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
/// Saves to the folder from settings (environment variables expanded; a relative folder lands under the user's
/// Pictures; empty means Pictures\ScrubShot), with the configured name pattern and a numbered suffix on collision.
/// The folder never depends on the process's current directory. The file is created with
/// <see cref="FileMode.CreateNew"/>, so a race with another writer can never overwrite an existing file, and a write
/// that fails or is cancelled deletes the file it started. Nothing is written anywhere else: no temp file, no copy (D-043).
/// </summary>
public sealed partial class CaptureSaver : ICaptureSaver
{
    private const int CreateAttempts = 3;

    private readonly Func<string, bool> _exists;

    public CaptureSaver()
        : this(File.Exists)
    {
    }

    /// <summary>For tests: an existence check that can disagree with the disk, to exercise the create race.</summary>
    internal CaptureSaver(Func<string, bool> exists)
    {
        _exists = exists;
    }

    public static CaptureSaver Instance { get; } = new();

    /// <summary>
    /// Resolves a settings value to an absolute folder, or says why it cannot be. Pure apart from reading
    /// environment variables; never consults the current directory.
    /// </summary>
    public static bool TryResolveFolder(string? configuredFolder, string? picturesFolder, out string folder, out SaveFolderProblem problem)
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

            folder = Path.GetFullPath(Path.Combine(picturesFolder, expanded.Length == 0 ? "ScrubShot" : expanded));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            problem = SaveFolderProblem.Invalid;
            return false;
        }
    }

    /// <summary>The folder a settings value resolves to. Throws an <see cref="IOException"/> whose message never contains the value.</summary>
    public static string ResolveFolder(string? configuredFolder, string? picturesFolder) =>
        TryResolveFolder(configuredFolder, picturesFolder, out var folder, out var problem)
            ? folder
            : throw new IOException(problem switch
            {
                SaveFolderProblem.NotFullyQualified => "The save folder must be an absolute path, or a folder name relative to Pictures.",
                SaveFolderProblem.PicturesUnavailable => "The Pictures folder is unavailable; set an absolute save folder.",
                SaveFolderProblem.UnresolvedVariable => "The save folder uses an environment variable that is not defined.",
                _ => "The save folder is not a valid path.",
            });

    /// <summary>The file name (with extension) for a capture under the configured pattern. Pure.</summary>
    public static string FileNameFor(OutputSettings settings, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var stem = FileNamePattern.Expand(settings.FileNamePattern, capturedAt);
        return stem + ImageEncoder.ExtensionFor(settings.Format);
    }

    public async Task<string> SaveAsync(ReadOnlyMemory<byte> encoded, OutputSettings settings, DateTimeOffset capturedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (encoded.IsEmpty)
        {
            throw new ArgumentException("Nothing to save.", nameof(encoded));
        }

        // DoNotVerify: a redirected Pictures folder that does not exist yet still comes back as a path, and
        // CreateDirectory below makes the whole chain. Without it a missing folder comes back empty.
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures, Environment.SpecialFolderOption.DoNotVerify);
        var folder = ResolveFolder(settings.SaveFolder, pictures);
        Directory.CreateDirectory(folder);
        var (file, path) = Create(folder, FileNameFor(settings, capturedAt));

        try
        {
            await using (file.ConfigureAwait(false))
            {
                await file.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // The handle is closed by now. A failed or cancelled write must not leave a truncated image behind,
            // and it is not retried under another name: a full disk or a dropped share is not a name problem.
            TryDelete(path);
            throw;
        }

        return path;
    }

    /// <summary>Creates the file under a free name. Only a lost race for the name is retried, never a write failure.</summary>
    private (FileStream File, string Path) Create(string folder, string fileName)
    {
        var lostRaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var attempt = 1; ; attempt++)
        {
            var path = UniqueFileName.Resolve(folder, fileName, candidate => lostRaces.Contains(candidate) || _exists(candidate));
            try
            {
                return (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1 << 16, FileOptions.Asynchronous), path);
            }
            catch (IOException) when (attempt < CreateAttempts && File.Exists(path))
            {
                // Another writer took the name between the existence check and CreateNew; its file is untouched.
                lostRaces.Add(path);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; the original failure is the one to report.
        }
    }

    [GeneratedRegex(@"%[^%\\/:*?""<>|]+%")]
    private static partial Regex UnexpandedVariable();
}
