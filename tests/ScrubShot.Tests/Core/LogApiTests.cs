// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Reflection;
using Microsoft.Extensions.Logging;
using ScrubShot.Core.Logging;

namespace ScrubShot.Tests.Core;

/// <summary>
/// Guards the "logs never contain captured content" rule at the API level (docs/decisions.md D-014).
/// If a log method ever accepts a string, object, span or buffer, this test fails the build.
/// </summary>
public class LogApiTests
{
    private static readonly HashSet<Type> AllowedParameterTypes =
    [
        typeof(ILogger),
        typeof(bool),
        typeof(int),
        typeof(long),
        typeof(double),
        typeof(TimeSpan),
        typeof(Version),
    ];

    [Fact]
    public void Log_exposes_at_least_one_method()
    {
        Assert.NotEmpty(LogMethods());
    }

    [Fact]
    public void Every_log_method_takes_only_event_shaped_parameters()
    {
        var offenders = LogMethods()
            .SelectMany(method => method.GetParameters().Select(parameter => (method, parameter)))
            .Where(pair => !IsAllowed(pair.parameter.ParameterType))
            .Select(pair => $"{pair.method.Name}({pair.parameter.Name}: {pair.parameter.ParameterType.Name})")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_log_method_takes_the_logger_first()
    {
        foreach (var method in LogMethods())
        {
            var first = method.GetParameters().FirstOrDefault();
            Assert.NotNull(first);
            Assert.Equal(typeof(ILogger), first.ParameterType);
        }
    }

    private static bool IsAllowed(Type type) => type.IsEnum || AllowedParameterTypes.Contains(type);

    private static IReadOnlyList<MethodInfo> LogMethods() =>
        typeof(Log)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToList();
}
