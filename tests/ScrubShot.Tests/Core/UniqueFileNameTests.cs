// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Output;

namespace ScrubShot.Tests.Core;

public class UniqueFileNameTests
{
    private const string Dir = @"C:\Shots";

    [Fact]
    public void Free_name_is_returned_unchanged()
    {
        var result = UniqueFileName.Resolve(Dir, "shot.png", _ => false);

        Assert.Equal(@"C:\Shots\shot.png", result);
    }

    [Fact]
    public void Collision_appends_a_counter_before_the_extension()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Shots\shot.png", @"C:\Shots\shot-2.png" };

        var result = UniqueFileName.Resolve(Dir, "shot.png", taken.Contains);

        Assert.Equal(@"C:\Shots\shot-3.png", result);
    }

    [Fact]
    public void Works_without_an_extension()
    {
        var result = UniqueFileName.Resolve(Dir, "shot", path => path.EndsWith("shot", StringComparison.Ordinal));

        Assert.Equal(@"C:\Shots\shot-2", result);
    }

    [Fact]
    public void Gives_up_after_the_attempt_limit()
    {
        Assert.Throws<IOException>(() => UniqueFileName.Resolve(Dir, "shot.png", _ => true));
    }
}
