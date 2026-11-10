// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;

namespace ScrubShot.Output;

/// <summary>Puts an image on the clipboard. The interface exists so the sink can be tested without touching the real clipboard.</summary>
public interface IClipboardWriter
{
    /// <summary>
    /// Replaces the clipboard content with the image as PNG and <c>CF_DIB</c>. With <paramref name="excludeFromHistory"/>
    /// the item is also marked so Windows keeps it out of clipboard history and cloud sync (D-012).
    /// </summary>
    void SetImage(ReadOnlySpan<byte> png, ReadOnlySpan<byte> dib, bool excludeFromHistory);
}

/// <summary>The format ids one clipboard write uses, resolved before the clipboard is opened.</summary>
internal readonly record struct ClipboardFormatIds(uint Png, uint Dib, uint ExcludeFromMonitor, uint CanIncludeInHistory, uint CanUploadToCloud);

/// <summary>An open clipboard that this process owns. The Win32 one is real; tests script failures through a fake.</summary>
internal interface IClipboardTarget
{
    void Empty();

    void Put(uint format, ReadOnlySpan<byte> data);
}

/// <summary>
/// Win32 clipboard writer. Opens the clipboard with a short retry (another app may hold it for a moment), owns it
/// through a throwaway message-only window because <c>OpenClipboard(NULL)</c> makes <c>SetClipboardData</c> fail,
/// and hands each buffer to the system as movable global memory, which the system then owns. The exclusion markers
/// go on before any pixels and a failure part-way empties the clipboard again, so a capture can never be left on
/// the clipboard without its markers (D-044).
/// </summary>
public sealed class ClipboardWriter : IClipboardWriter
{
    public const int OpenAttempts = 10;
    public static readonly TimeSpan OpenRetryDelay = TimeSpan.FromMilliseconds(25);

    public static ClipboardWriter Instance { get; } = new();

    /// <summary>Number of OpenClipboard attempts the last call needed. For diagnostics, never for behavior.</summary>
    public int LastOpenAttempts { get; private set; }

    public unsafe void SetImage(ReadOnlySpan<byte> png, ReadOnlySpan<byte> dib, bool excludeFromHistory)
    {
        if (png.IsEmpty || dib.IsEmpty)
        {
            throw new ArgumentException("Both the PNG and the DIB payload are required.");
        }

        // Resolve every format id first: a registration failure must leave the user's current clipboard untouched.
        // RegisterClipboardFormat returns the same id for the same name, so there is nothing to cache.
        var ids = new ClipboardFormatIds(
            Register("PNG"),
            (uint)CLIPBOARD_FORMAT.CF_DIB,
            Register("ExcludeClipboardContentFromMonitorProcessing"),
            Register("CanIncludeInClipboardHistory"),
            Register("CanUploadToCloudClipboard"));

        // A message-only window of the system STATIC class; it exists only for this call and only to own the clipboard.
        var owner = PInvoke.CreateWindowEx(0, "STATIC", null, 0, 0, 0, 0, 0, HWND.HWND_MESSAGE, null, null, null);
        if (owner.IsNull)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the clipboard owner window.");
        }

        try
        {
            Open(owner);
            try
            {
                Write(Win32Target.Instance, ids, png, dib, excludeFromHistory);
            }
            finally
            {
                PInvoke.CloseClipboard();
            }
        }
        finally
        {
            PInvoke.DestroyWindow(owner);
        }
    }

    /// <summary>
    /// The write sequence on an open clipboard. Order matters for privacy, not for pasting: the three exclusion
    /// markers are placed before any pixels, so if a later step fails the item that history and cloud sync would
    /// see is already marked; and any failure empties the clipboard again so nothing half-written is left behind.
    /// Among the content formats the most descriptive (PNG) comes first, as Learn asks.
    /// </summary>
    internal static void Write(IClipboardTarget target, ClipboardFormatIds ids, ReadOnlySpan<byte> png, ReadOnlySpan<byte> dib, bool excludeFromHistory)
    {
        target.Empty();
        try
        {
            if (excludeFromHistory)
            {
                ReadOnlySpan<byte> zero = [0, 0, 0, 0];
                target.Put(ids.ExcludeFromMonitor, zero);   // any data: keep every format out of history and sync
                target.Put(ids.CanIncludeInHistory, zero);  // DWORD 0: not in clipboard history
                target.Put(ids.CanUploadToCloud, zero);     // DWORD 0: not synced to other devices
            }

            target.Put(ids.Png, png);
            target.Put(ids.Dib, dib);
        }
        catch
        {
            try
            {
                target.Empty();
            }
            catch (Win32Exception)
            {
                // Nothing better is possible; the original failure is the one to report.
            }

            throw;
        }
    }

    private void Open(HWND owner)
    {
        for (var attempt = 1; attempt <= OpenAttempts; attempt++)
        {
            if (PInvoke.OpenClipboard(owner))
            {
                LastOpenAttempts = attempt;
                return;
            }

            var error = Marshal.GetLastWin32Error();
            if (attempt == OpenAttempts)
            {
                LastOpenAttempts = attempt;
                throw new Win32Exception(error, "The clipboard is held open by another application.");
            }

            Thread.Sleep(OpenRetryDelay);
        }
    }

    private static uint Register(string name)
    {
        var id = PInvoke.RegisterClipboardFormat(name);
        return id != 0 ? id : throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClipboardFormat failed.");
    }

    private sealed class Win32Target : IClipboardTarget
    {
        public static Win32Target Instance { get; } = new();

        public void Empty()
        {
            if (!PInvoke.EmptyClipboard())
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "EmptyClipboard failed.");
            }
        }

        public unsafe void Put(uint format, ReadOnlySpan<byte> data)
        {
            var handle = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, (nuint)data.Length);
            if (handle.IsNull)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GlobalAlloc failed for a clipboard buffer.");
            }

            try
            {
                var target = PInvoke.GlobalLock(handle);
                if (target is null)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GlobalLock failed for a clipboard buffer.");
                }

                try
                {
                    data.CopyTo(new Span<byte>(target, data.Length));
                }
                finally
                {
                    PInvoke.GlobalUnlock(handle); // returns FALSE with NO_ERROR when the count reaches zero; not a failure
                }

                if (PInvoke.SetClipboardData(format, new HANDLE(handle.Value)).IsNull)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetClipboardData failed.");
                }

                handle = default; // the system owns the memory now
            }
            finally
            {
                if (!handle.IsNull)
                {
                    PInvoke.GlobalFree(handle);
                }
            }
        }
    }
}
