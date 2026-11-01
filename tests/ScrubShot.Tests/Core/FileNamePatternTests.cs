// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Output;

namespace ScrubShot.Tests.Core;

public class FileNamePatternTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 11, 1, 9, 5, 7, TimeSpan.Zero);

    [Fact]
    public void Default_pattern_sorts_chronologically()
    {
        Assert.Equal("2026-11-01_09-05-07", FileNamePattern.Expand(FileNamePattern.DefaultPattern, Stamp));
    }

    [Fact]
    public void Hours_are_24_hour()
    {
        var afternoon = new DateTimeOffset(2026, 11, 1, 15, 0, 0, TimeSpan.Zero);

        Assert.Equal("15", FileNamePattern.Expand("${hh}", afternoon));
    }

    [Fact]
    public void Sequence_number_is_zero_padded()
    {
        Assert.Equal("shot-0042", FileNamePattern.Expand("shot-${NUM}", Stamp, sequenceNumber: 42));
        Assert.Equal("shot-12345", FileNamePattern.Expand("shot-${NUM}", Stamp, sequenceNumber: 12345));
    }

    [Fact]
    public void Title_token_expands_when_supplied_and_is_sanitized()
    {
        Assert.Equal("Settings _ Notepad", FileNamePattern.Expand("${title}", Stamp, title: "Settings | Notepad"));
        Assert.Equal("2026-", FileNamePattern.Expand("${YYYY}-${title}", Stamp, title: null));
    }

    [Fact]
    public void Tokens_are_case_sensitive_and_unknown_tokens_stay_literal()
    {
        Assert.Equal("${yyyy}-${Foo}", FileNamePattern.Expand("${yyyy}-${Foo}", Stamp));
    }

    [Theory]
    [InlineData(@"a<b>c:d""e/f\g|h?i*j", "a_b_c_d_e_f_g_h_i_j")]
    [InlineData("name.", "name")]
    [InlineData("  padded  ", "padded")]
    [InlineData("trail. . .", "trail")]
    [InlineData("tab\there", "tab_here")]
    public void Invalid_characters_and_trailing_dots_are_cleaned(string input, string expected)
    {
        Assert.Equal(expected, FileNamePattern.Sanitize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData(". .")]
    public void Empty_results_fall_back_to_a_fixed_name(string input)
    {
        Assert.Equal(FileNamePattern.FallbackName, FileNamePattern.Expand(input, Stamp));
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("lpt1", "_lpt1")]
    [InlineData("aux", "_aux")]
    [InlineData("nul.backup", "_nul.backup")]
    [InlineData("con.txt - Notepad", "_con.txt - Notepad")]
    [InlineData("COM¹", "_COM¹")]
    [InlineData("LPT³.x", "_LPT³.x")]
    [InlineData("CONSOLE", "CONSOLE")]
    [InlineData("CONSOLE.txt", "CONSOLE.txt")]
    [InlineData("my.con", "my.con")]
    public void Reserved_device_names_are_prefixed(string input, string expected)
    {
        Assert.Equal(expected, FileNamePattern.Sanitize(input));
    }

    [Fact]
    public void Length_is_capped()
    {
        var longName = new string('x', 500);

        var result = FileNamePattern.Sanitize(longName);

        Assert.Equal(FileNamePattern.MaxLength, result.Length);
    }

    [Fact]
    public void Length_cap_never_splits_a_surrogate_pair()
    {
        var name = new string('x', FileNamePattern.MaxLength - 1) + "\U0001F4F7" + "tail";

        var result = FileNamePattern.Sanitize(name);

        Assert.Equal(FileNamePattern.MaxLength - 1, result.Length);
        Assert.False(char.IsSurrogate(result[^1]));
    }
}
