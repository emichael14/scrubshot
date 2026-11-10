// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using ScrubShot.Core.Logging;

namespace ScrubShot.Core.Lifecycle;

/// <summary>Where the launch-at-startup command line is stored. The registry implementation is the real one; tests use memory.</summary>
public interface IStartupStore
{
    /// <summary>The current command line, or null when ScrubShot is not registered.</summary>
    string? Read();

    void Write(string commandLine);

    void Remove();
}

/// <summary>The per-user Run key: <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, value <c>ScrubShot</c>. No elevation needed.</summary>
public sealed class RegistryStartupStore : IStartupStore
{
    public const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "ScrubShot";

    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: false);
        return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void Write(string commandLine)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunSubKey, writable: true)
            ?? throw new IOException("The Run key could not be opened for writing.");
        key.SetValue(ValueName, commandLine, RegistryValueKind.String);
    }

    public void Remove()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

public sealed class InMemoryStartupStore : IStartupStore
{
    public string? CommandLine { get; set; }

    public int Writes { get; private set; }

    public int Removes { get; private set; }

    public string? Read() => CommandLine;

    public void Write(string commandLine)
    {
        CommandLine = commandLine;
        Writes++;
    }

    public void Remove()
    {
        CommandLine = null;
        Removes++;
    }
}

public enum StartupSyncResult
{
    /// <summary>The store already matched the setting.</summary>
    Unchanged,

    /// <summary>The value was added.</summary>
    Registered,

    /// <summary>The value existed with a different command line (an older install path) and was rewritten.</summary>
    Updated,

    /// <summary>The value was removed because the setting is off.</summary>
    Removed,

    /// <summary>The command line would exceed the documented limit; nothing was written.</summary>
    CommandLineTooLong,

    /// <summary>The store could not be read or written.</summary>
    Failed,
}

/// <summary>
/// Keeps the Run key in step with the effective <c>General.LaunchAtStartup</c> setting (D-041). Called at every launch,
/// so a policy value is enforced even if someone edited the key by hand, and a user who turned the setting off has
/// the entry removed. Windows' own per-app startup switch (Settings &gt; Apps &gt; Startup) is left alone.
/// </summary>
public static class StartupRegistration
{
    /// <summary>Documented limit for a Run value's command line (Learn: Run and RunOnce Registry Keys).</summary>
    public const int MaxCommandLineLength = 260;

    public static string CommandLineFor(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return $"\"{executablePath}\"";
    }

    public static StartupSyncResult Sync(bool launchAtStartup, string executablePath, IStartupStore store, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        logger ??= NullLogger.Instance;
        var desired = launchAtStartup ? CommandLineFor(executablePath) : null;
        if (desired is { Length: > MaxCommandLineLength })
        {
            Log.StartupRegistrationFailed(logger, StartupSyncResult.CommandLineTooLong, 0);
            return StartupSyncResult.CommandLineTooLong;
        }

        StartupSyncResult result;
        try
        {
            var current = store.Read();
            if (desired is null)
            {
                if (current is null)
                {
                    result = StartupSyncResult.Unchanged;
                }
                else
                {
                    store.Remove();
                    result = StartupSyncResult.Removed;
                }
            }
            else if (string.Equals(current, desired, StringComparison.OrdinalIgnoreCase))
            {
                result = StartupSyncResult.Unchanged;
            }
            else
            {
                store.Write(desired);
                result = current is null ? StartupSyncResult.Registered : StartupSyncResult.Updated;
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.StartupRegistrationFailed(logger, StartupSyncResult.Failed, ex.HResult);
            return StartupSyncResult.Failed;
        }

        if (result != StartupSyncResult.Unchanged)
        {
            Log.StartupRegistrationSynced(logger, result);
        }

        return result;
    }
}
