// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Security;
using Microsoft.Win32;

namespace ScrubShot.Core.Settings;

/// <summary>
/// Reads admin policy from <c>Software\Policies\ScrubShot</c> under a registry hive. Machine policy lives
/// under HKLM, user policy under HKCU; Intune, Group Policy and any MDM can write both.
/// </summary>
/// <remarks>
/// An absent key is the only thing that means "no policy". Any failure while reading (access denied, the key
/// deleted or rewritten under us by a policy refresh, one value that cannot be read) is reported as a
/// <see cref="PolicyReadException"/> rather than as a shorter list, so the caller fails closed and keeps the
/// locks it already had (D-048).
/// </remarks>
public sealed class RegistryPolicySource : IPolicySource
{
    public const string PolicySubKey = @"Software\Policies\ScrubShot";

    private readonly RegistryHive _hive;
    private readonly string _subKey;

    public RegistryPolicySource(RegistryHive hive, string subKey = PolicySubKey)
    {
        _hive = hive;
        _subKey = subKey;
    }

    public static RegistryPolicySource Machine() => new(RegistryHive.LocalMachine);

    public static RegistryPolicySource User() => new(RegistryHive.CurrentUser);

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
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            throw new PolicyReadException(ex);
        }

        return values;
    }
}
