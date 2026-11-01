// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using ScrubShot.Core.Logging;

namespace ScrubShot.Core.Settings;

/// <summary>
/// Reads admin policy from <c>Software\Policies\ScrubShot</c> under a registry hive. Machine policy lives
/// under HKLM, user policy under HKCU; Intune, Group Policy and any MDM can write both.
/// </summary>
public sealed class RegistryPolicySource : IPolicySource
{
    public const string PolicySubKey = @"Software\Policies\ScrubShot";

    private readonly RegistryHive _hive;
    private readonly string _subKey;
    private readonly ILogger _logger;

    public RegistryPolicySource(RegistryHive hive, ILogger? logger = null, string subKey = PolicySubKey)
    {
        _hive = hive;
        _subKey = subKey;
        _logger = logger ?? NullLogger.Instance;
    }

    public static RegistryPolicySource Machine(ILogger? logger = null) => new(RegistryHive.LocalMachine, logger);

    public static RegistryPolicySource User(ILogger? logger = null) => new(RegistryHive.CurrentUser, logger);

    public IReadOnlyDictionary<string, object> Read()
    {
        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(_hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(_subKey, writable: false);
            if (key is null)
            {
                return values;
            }

            foreach (var name in key.GetValueNames())
            {
                if (name.Length == 0)
                {
                    continue;
                }

                try
                {
                    switch (key.GetValueKind(name))
                    {
                        case RegistryValueKind.DWord when key.GetValue(name) is int dword:
                            values[name] = dword;
                            break;
                        case RegistryValueKind.String or RegistryValueKind.ExpandString when key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string text:
                            values[name] = text;
                            break;
                    }
                }
                catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
                {
                    // A value removed between enumeration and read (a policy refresh rewrites the key). Skip it, keep the rest.
                    Log.PolicyReadFailed(_logger, ex.HResult);
                }
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.PolicyReadFailed(_logger, ex.HResult);
        }

        return values;
    }
}
