// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

namespace ScrubShot.Core.Settings;

/// <summary>
/// A set of admin policy values keyed by setting path (for example "Output.DefaultAction").
/// Values are <see cref="int"/> (REG_DWORD) or <see cref="string"/> (REG_SZ, REG_EXPAND_SZ); anything else is dropped.
/// </summary>
public interface IPolicySource
{
    /// <summary>
    /// Returns every policy value, or throws <see cref="PolicyReadException"/> when the policy could not be read
    /// completely. An empty result means "no policy" and nothing else: a source must never return a partial set,
    /// because a missing value would silently unlock the setting it was meant to fix.
    /// </summary>
    IReadOnlyDictionary<string, object> Read();
}

/// <summary>
/// The policy could not be read completely (access denied, the key was being rewritten by a policy refresh, a value
/// vanished between enumeration and read). The caller keeps the locks it already knows and tries again later.
/// </summary>
public sealed class PolicyReadException : Exception
{
    public PolicyReadException(Exception cause)
        : base("Policy could not be read completely.", cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        HResult = cause.HResult;
    }
}

/// <summary>Policy values held in memory. Used by tests and when a hive is absent.</summary>
public sealed class InMemoryPolicySource : IPolicySource
{
    private readonly IReadOnlyDictionary<string, object> _values;

    public InMemoryPolicySource(IReadOnlyDictionary<string, object> values)
    {
        _values = values;
    }

    public static InMemoryPolicySource Empty { get; } = new(new Dictionary<string, object>());

    public IReadOnlyDictionary<string, object> Read() => _values;
}
