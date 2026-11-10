// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Output;
using ScrubShot.Core.Settings;

namespace ScrubShot.Output;

/// <summary>Writes encoded bytes to a file. The interface exists so the sink can be tested without touching the disk.</summary>
public interface ICaptureSaver
{
    /// <summary>Writes <paramref name="encoded"/> into the configured folder under the configured pattern and returns the full path.</summary>
    Task<string> SaveAsync(ReadOnlyMemory<byte> encoded, OutputSettings settings, DateTimeOffset capturedAt, CancellationToken cancellationToken);
}

/// <summary>
/// Saves to the folder from settings (environment variables expanded; a relative folder lands under the user's
/// Pictures; empty means Pictures\ScrubShot; see <see cref="SaveFolder"/>), with the configured name pattern and a numbered suffix on collision.
/// The folder never depends on the process's current directory. The file is created with
/// <see cref="FileMode.CreateNew"/>, so a race with another writer can never overwrite an existing file, and a write
/// that fails or is cancelled deletes the file it started. Nothing is written anywhere else: no temp file, no copy (D-043).
/// </summary>
public sealed class CaptureSaver : ICaptureSaver
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

        // The Pictures folder is asked for without verification: a redirected folder that does not exist yet still
        // comes back as a path, and CreateDirectory below makes the whole chain.
        var folder = SaveFolder.Resolve(settings.SaveFolder, SaveFolder.PicturesFolder());
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
}
