// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.ComponentModel;
using ScrubShot.Core.Logging;

namespace ScrubShot.Tests.Core;

public class ErrorCodesTests
{
    [Fact]
    public void A_win32_exception_reports_its_native_code_as_an_hresult_not_e_fail()
    {
        var exception = new Win32Exception(1418, "The clipboard is held open by another application.");

        Assert.Equal(unchecked((int)0x80004005), exception.HResult); // what logging ex.HResult would have recorded
        Assert.Equal(unchecked((int)0x8007058A), ErrorCodes.HResultOf(exception));
    }

    [Fact]
    public void A_wrapped_win32_exception_is_found_through_the_inner_chain()
    {
        var wrapped = new InvalidOperationException("The hotkey service could not start.", new Win32Exception(1410));

        Assert.Equal(unchecked((int)0x80070582), ErrorCodes.HResultOf(wrapped));
    }

    [Fact]
    public void Other_exceptions_keep_their_own_hresult()
    {
        var exception = new FileNotFoundException();

        Assert.Equal(exception.HResult, ErrorCodes.HResultOf(exception));
        Assert.Equal(unchecked((int)0x80070002), ErrorCodes.HResultOf(exception));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, unchecked((int)0x80070005))]
    [InlineData(unchecked((int)0x80070005), unchecked((int)0x80070005))]
    public void From_win32_matches_the_macro(int error, int expected)
    {
        Assert.Equal(expected, ErrorCodes.FromWin32(error));
    }
}
