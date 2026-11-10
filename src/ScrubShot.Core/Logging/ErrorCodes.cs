// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.ComponentModel;

namespace ScrubShot.Core.Logging;

/// <summary>
/// Turns an exception into the one number the log is allowed to carry. A <see cref="Win32Exception"/> reports
/// E_FAIL as its HResult and keeps the real code in <see cref="Win32Exception.NativeErrorCode"/>, so logging
/// <c>ex.HResult</c> directly would make every Win32 failure look the same.
/// </summary>
public static class ErrorCodes
{
    private const uint Win32Facility = 0x80070000;

    /// <summary>The HRESULT for <paramref name="exception"/>: HRESULT_FROM_WIN32 of the first Win32 error in the chain, else the exception's own HResult.</summary>
    public static int HResultOf(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is Win32Exception win32)
            {
                return FromWin32(win32.NativeErrorCode);
            }
        }

        return exception.HResult;
    }

    /// <summary>HRESULT_FROM_WIN32: zero and values that are already HRESULTs pass through unchanged.</summary>
    public static int FromWin32(int error) =>
        error <= 0 ? error : unchecked((int)(Win32Facility | ((uint)error & 0xFFFF)));
}
