// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Reflection;

namespace ScrubShot.Tests.Core;

/// <summary>
/// Guards the "local only" rule at the assembly level: the shipped libraries must not reference any networking
/// assembly. A reference appears as soon as code uses a type from one, so this fails the build the day someone
/// adds an HTTP call, a socket or a mail client anywhere in these projects. The one outbound call the spec allows
/// (the optional update check, a later milestone) will live in the app project behind a setting and a policy, and
/// this list is where its assembly will have to be named explicitly.
/// </summary>
public class LocalOnlyTests
{
    private static readonly Assembly[] ShippedLibraries =
    [
        typeof(ScrubShot.Core.Settings.ScrubShotSettings).Assembly,
        typeof(ScrubShot.Capture.FrozenScreen).Assembly,
        typeof(ScrubShot.Output.OutputSink).Assembly,
    ];

    private static readonly string[] NetworkingPrefixes =
    [
        "System.Net.Http",
        "System.Net.Sockets",
        "System.Net.Requests",
        "System.Net.WebClient",
        "System.Net.WebSockets",
        "System.Net.Mail",
        "System.Net.Quic",
        "System.Net.NameResolution",
        "System.Net.NetworkInformation",
        "System.Net.Ping",
        "System.Net.Security",
        "System.Net.HttpListener",
        "System.Net.WebProxy",
        "System.Net.ServicePoint",
        "Microsoft.AspNetCore",
        "Grpc",
        "Azure.",
        "Microsoft.ApplicationInsights",
        "Sentry",
    ];

    [Fact]
    public void The_three_shipped_libraries_are_the_ones_under_test()
    {
        Assert.Equal(["ScrubShot.Capture", "ScrubShot.Core", "ScrubShot.Output"], ShippedLibraries.Select(a => a.GetName().Name).Order());
    }

    [Fact]
    public void No_shipped_library_references_a_networking_assembly()
    {
        var offenders = ShippedLibraries
            .SelectMany(library => library.GetReferencedAssemblies().Select(reference => (Library: library.GetName().Name, Reference: reference.Name ?? string.Empty)))
            .Where(pair => NetworkingPrefixes.Any(prefix => pair.Reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Select(pair => $"{pair.Library} -> {pair.Reference}")
            .ToList();

        Assert.Empty(offenders);
    }
}
