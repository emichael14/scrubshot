// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using ScrubShot.Core.Geometry;
using ScrubShot.Core.Selection;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ScrubShot.Capture.Overlay;

/// <summary>
/// The selection overlay: one borderless topmost window per monitor, each showing that monitor's slice of
/// the frozen screen dimmed, with the live selection drawn bright. All windows share one
/// <see cref="RegionSelection"/> so a drag can cross monitors. Runs a modal message loop on the calling
/// thread; use <see cref="RunOnNewThreadAsync"/> from a UI thread. No XAML, no files, physical pixels only.
/// </summary>
public sealed unsafe class OverlaySession : IDisposable
{
    private const string ClassName = "ScrubShotOverlay";
    private const int DimPercent = 45;
    private const int MagnifierZoom = 4;
    private const int MagnifierSourcePixels = 31; // odd, so the cursor pixel is centered

    private static readonly COLORREF Accent = new(0x00A69F0E);       // aqua/teal in BGR (0x0E9FA6 RGB)
    private static readonly COLORREF LabelBackground = new(0x00302A1A);
    private static readonly COLORREF LabelText = new(0x00FFFFFF);

    [ThreadStatic]
    private static Dictionary<nint, OverlaySession>? windowsOnThread;

    private static readonly WNDPROC WindowProcedure = WndProc;
    private static ushort classAtom;

    private readonly FrozenScreen _frozen;
    private readonly OverlayMode _mode;
    private readonly OverlayOptions _options;
    private readonly RegionSelection _selection;
    private readonly List<MonitorWindow> _windows = [];
    private readonly IReadOnlyList<WindowCandidate> _candidates;
    private HDC _frozenDc;
    private HGDIOBJ _frozenPrevious;
    private HPEN _accentPen;
    private HBRUSH _labelBrush;
    private bool _magnifier;
    private OverlayResult? _result;
    private WindowCandidate? _hover;

    private OverlaySession(FrozenScreen frozen, OverlayMode mode, OverlayOptions options)
    {
        _frozen = frozen;
        _mode = mode;
        _options = options;
        _magnifier = options.ShowMagnifier;
        _selection = new RegionSelection(frozen.Layout.VirtualScreen);
        _candidates = mode == OverlayMode.Window ? WindowFinder.GetCandidates() : [];
        if (options.InitialRegion is { } initial && LastRegion.Reconcile(initial, frozen.Layout.VirtualScreen) is { } region)
        {
            _selection.Select(region);
        }
    }

    /// <summary>Shows the overlay and blocks until the user confirms or cancels. Must run on a thread that owns no other message loop.</summary>
    public static OverlayResult Run(FrozenScreen frozen, OverlayMode mode, OverlayOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(frozen);
        using var dpiScope = ProcessDpi.EnterPerMonitorV2();
        using var session = new OverlaySession(frozen, mode, options ?? OverlayOptions.Default);
        return session.RunLoop();
    }

    /// <summary>Runs the overlay on a dedicated STA thread and completes when it closes.</summary>
    public static Task<OverlayResult> RunOnNewThreadAsync(FrozenScreen frozen, OverlayMode mode, OverlayOptions? options = null)
    {
        var completion = new TaskCompletionSource<OverlayResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(Run(frozen, mode, options));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            Name = "ScrubShot overlay",
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private OverlayResult RunLoop()
    {
        EnsureClassRegistered();
        windowsOnThread ??= [];

        _frozenDc = PInvoke.CreateCompatibleDC(HDC.Null);
        if (_frozenDc.IsNull)
        {
            throw new Win32Exception("CreateCompatibleDC failed.");
        }

        _frozenPrevious = PInvoke.SelectObject(_frozenDc, _frozen.BitmapHandle);
        _accentPen = PInvoke.CreatePen(PEN_STYLE.PS_SOLID, 1, Accent);
        _labelBrush = PInvoke.CreateSolidBrush(LabelBackground);

        foreach (var monitor in _frozen.Layout.Monitors)
        {
            _windows.Add(new MonitorWindow(this, monitor));
        }

        var cursor = CurrentCursor();
        _selection.MoveCursor(cursor);
        UpdateHover();

        // Show every window only once all are prepared, so no monitor flashes an unpainted topmost window.
        foreach (var window in _windows)
        {
            PInvoke.ShowWindow(window.Handle, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        }

        var under = _windows.FirstOrDefault(w => w.Monitor.Bounds.Contains(cursor)) ?? _windows[0];
        PInvoke.SetForegroundWindow(under.Handle);
        PInvoke.SetFocus(under.Handle);
        InvalidateAll();

        MSG message;
        while (PInvoke.GetMessage(&message, HWND.Null, 0, 0))
        {
            PInvoke.TranslateMessage(&message);
            PInvoke.DispatchMessage(&message);
        }

        return _result ?? OverlayResult.Cancelled;
    }

    public void Dispose()
    {
        foreach (var window in _windows)
        {
            window.Dispose();
        }

        _windows.Clear();

        if (!_frozenDc.IsNull)
        {
            PInvoke.SelectObject(_frozenDc, _frozenPrevious);
            PInvoke.DeleteDC(_frozenDc);
            _frozenDc = HDC.Null;
        }

        if (!_accentPen.IsNull)
        {
            PInvoke.DeleteObject(_accentPen);
            _accentPen = HPEN.Null;
        }

        if (!_labelBrush.IsNull)
        {
            PInvoke.DeleteObject(_labelBrush);
            _labelBrush = HBRUSH.Null;
        }
    }

    private static void EnsureClassRegistered()
    {
        if (classAtom != 0)
        {
            return;
        }

        var module = PInvoke.GetModuleHandle(default(PCWSTR));
        fixed (char* name = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                style = WNDCLASS_STYLES.CS_HREDRAW | WNDCLASS_STYLES.CS_VREDRAW,
                lpfnWndProc = WindowProcedure,
                hInstance = new HINSTANCE(module.Value),
                hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_CROSS),
                lpszClassName = new PCWSTR(name),
            };
            classAtom = PInvoke.RegisterClassEx(in wc);
        }

        if (classAtom == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx failed.");
        }
    }

    private static PixelPoint CurrentCursor()
    {
        PInvoke.GetCursorPos(out var point);
        return new PixelPoint(point.X, point.Y);
    }

    /// <summary>The position carried by a mouse message (client coordinates, signed) in virtual-screen pixels.</summary>
    private static PixelPoint MessageCursor(MonitorWindow window, LPARAM lParam)
    {
        var raw = (uint)(nint)lParam.Value;
        var x = (short)(raw & 0xFFFF);
        var y = (short)(raw >> 16);
        return new PixelPoint(window.Monitor.Bounds.Left + x, window.Monitor.Bounds.Top + y);
    }

    /// <summary>Thread key state first (synchronized with the message being handled), physical state as a fallback.</summary>
    private static bool ShiftIsDown() =>
        (PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_SHIFT) & 0x8000) != 0
        || (PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_SHIFT) & 0x8000) != 0;

    private void Finish(OverlayResult result)
    {
        if (_result is not null)
        {
            return;
        }

        _result = result;
        PInvoke.ReleaseCapture();
        PInvoke.PostQuitMessage(0);
    }

    private void Confirm()
    {
        if (_mode == OverlayMode.Window)
        {
            if (_hover is { } picked)
            {
                var frame = WindowPick.CapturableFrame(picked, _frozen.Layout.VirtualScreen);
                if (!frame.IsEmpty)
                {
                    Finish(new OverlayResult(OverlayOutcome.Confirmed, frame, picked.Handle));
                }
            }

            return;
        }

        if (_selection.Confirm() is { } region)
        {
            Finish(new OverlayResult(OverlayOutcome.Confirmed, region));
        }
    }

    private void UpdateHover()
    {
        if (_mode != OverlayMode.Window)
        {
            return;
        }

        var pointer = _selection.Cursor;
        var hit = WindowPick.HitTest(_candidates, pointer);
        if (hit != _hover)
        {
            _hover = hit;
            if (hit is null)
            {
                _selection.Cancel();
            }
            else
            {
                _selection.Select(WindowPick.CapturableFrame(hit, _frozen.Layout.VirtualScreen));
                _selection.MoveCursor(pointer); // Select parks the cursor at the far corner; keep the real pointer
            }
        }
    }

    private void InvalidateAll()
    {
        foreach (var window in _windows)
        {
            PInvoke.InvalidateRect(window.Handle, (RECT*)null, false);
        }
    }

    private LRESULT HandleMessage(MonitorWindow window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (_result is not null && message is not (PInvoke.WM_PAINT or PInvoke.WM_ERASEBKGND or PInvoke.WM_DESTROY))
        {
            // The outcome is decided; input that was already queued must not change it.
            return PInvoke.DefWindowProc(window.Handle, message, wParam, lParam);
        }

        switch (message)
        {
            case PInvoke.WM_ERASEBKGND:
                return new LRESULT(1);

            case PInvoke.WM_PAINT:
                window.Paint();
                return new LRESULT(0);

            case PInvoke.WM_MOUSEMOVE:
                _selection.MoveCursor(MessageCursor(window, lParam));
                UpdateHover();
                InvalidateAll();
                return new LRESULT(0);

            case PInvoke.WM_LBUTTONDOWN:
                if (_mode == OverlayMode.Window)
                {
                    Confirm();
                    return new LRESULT(0);
                }

                var point = MessageCursor(window, lParam);
                if (_selection.State == SelectionState.Selected && _selection.Rect.Contains(point))
                {
                    // A click inside a selected region (Enter-to-confirm mode, or a remembered region) confirms it.
                    Confirm();
                    return new LRESULT(0);
                }

                PInvoke.SetCapture(window.Handle);
                _selection.BeginDrag(point);
                InvalidateAll();
                return new LRESULT(0);

            case PInvoke.WM_LBUTTONUP:
                if (_mode == OverlayMode.Region && _selection.State == SelectionState.Dragging)
                {
                    PInvoke.ReleaseCapture();
                    _selection.EndDrag(MessageCursor(window, lParam));
                    if (_options.ConfirmOnRelease)
                    {
                        Confirm();
                    }

                    InvalidateAll();
                }
                else if (_mode == OverlayMode.Region && _selection.State == SelectionState.Selected)
                {
                    Confirm();
                }

                return new LRESULT(0);

            case PInvoke.WM_RBUTTONUP:
                Finish(OverlayResult.Cancelled);
                return new LRESULT(0);

            case PInvoke.WM_KEYDOWN:
                HandleKey((VIRTUAL_KEY)(ushort)wParam.Value);
                return new LRESULT(0);

            case PInvoke.WM_CLOSE:
                Finish(OverlayResult.Cancelled);
                return new LRESULT(0);

            default:
                return PInvoke.DefWindowProc(window.Handle, message, wParam, lParam);
        }
    }

    private void HandleKey(VIRTUAL_KEY key)
    {
        var step = ShiftIsDown() ? 10 : 1;
        switch (key)
        {
            case VIRTUAL_KEY.VK_ESCAPE:
                Finish(OverlayResult.Cancelled);
                break;
            case VIRTUAL_KEY.VK_RETURN or VIRTUAL_KEY.VK_SPACE:
                if (_mode == OverlayMode.Region && _selection.State == SelectionState.Idle)
                {
                    _selection.BeginDrag(_selection.Cursor);
                }
                else if (_mode == OverlayMode.Region && _selection.State == SelectionState.Dragging)
                {
                    _selection.EndDrag(_selection.Cursor);
                    if (_selection.State == SelectionState.Selected)
                    {
                        Confirm();
                    }
                }
                else
                {
                    Confirm();
                }

                break;
            case VIRTUAL_KEY.VK_LEFT:
                Arrow(-step, 0);
                break;
            case VIRTUAL_KEY.VK_RIGHT:
                Arrow(step, 0);
                break;
            case VIRTUAL_KEY.VK_UP:
                Arrow(0, -step);
                break;
            case VIRTUAL_KEY.VK_DOWN:
                Arrow(0, step);
                break;
            case VIRTUAL_KEY.VK_M:
                _magnifier = !_magnifier;
                break;
            default:
                return;
        }

        UpdateHover();
        InvalidateAll();
    }

    /// <summary>Arrows size the selection in region mode and move the pointer (re-hit-testing) in window mode.</summary>
    private void Arrow(int dx, int dy)
    {
        if (_mode == OverlayMode.Window)
        {
            _selection.MoveCursor(_selection.Cursor.Offset(dx, dy));
        }
        else
        {
            _selection.Nudge(dx, dy);
        }
    }

    private static LRESULT WndProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (windowsOnThread is { } map && map.TryGetValue((nint)hwnd.Value, out var session))
        {
            var window = session._windows.FirstOrDefault(w => w.Handle == hwnd);
            if (window is not null)
            {
                return session.HandleMessage(window, message, wParam, lParam);
            }
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    /// <summary>One overlay window covering one monitor, with a dimmed base image, a back buffer and DPI-scaled chrome.</summary>
    private sealed class MonitorWindow : IDisposable
    {
        private static readonly byte[] DimTable = Enumerable.Range(0, 256).Select(v => (byte)(v * DimPercent / 100)).ToArray();

        private readonly OverlaySession _session;
        private readonly double _scale;
        private HDC _backDc;
        private HBITMAP _backBitmap;
        private HGDIOBJ _backPrevious;
        private HDC _dimDc;
        private HBITMAP _dimBitmap;
        private HGDIOBJ _dimPrevious;
        private HFONT _font;
        private bool _ownsFont;

        // Both buffers hold legible screen pixels (the dimmed copy and the composited frame), so their bits are
        // kept in order to zero them on dispose, like the frozen screen itself (D-036).
        private byte* _backBits;
        private byte* _dimBits;
        private int _bufferBytes;

        public MonitorWindow(OverlaySession session, MonitorInfo monitor)
        {
            _session = session;
            Monitor = monitor;
            _scale = monitor.Scale;
            var bounds = monitor.Bounds;

            Handle = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
                ClassName,
                null,
                WINDOW_STYLE.WS_POPUP,
                bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                HWND.Null, null, null, null);

            if (Handle.IsNull)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx failed for the overlay.");
            }

            windowsOnThread![(nint)Handle.Value] = session;

            try
            {
                _backDc = PInvoke.CreateCompatibleDC(session._frozenDc);
                _dimDc = PInvoke.CreateCompatibleDC(session._frozenDc);
                if (_backDc.IsNull || _dimDc.IsNull)
                {
                    throw new Win32Exception("CreateCompatibleDC failed for the overlay buffers.");
                }

                _bufferBytes = checked(bounds.Width * bounds.Height * 4);
                _backBitmap = CreateDib(bounds.Width, bounds.Height, out _backBits);
                _dimBitmap = CreateDib(bounds.Width, bounds.Height, out _dimBits);
                var dimBits = _dimBits;
                _backPrevious = PInvoke.SelectObject(_backDc, _backBitmap);
                _dimPrevious = PInvoke.SelectObject(_dimDc, _dimBitmap);

                // Dimmed base image: copy this monitor's slice, then scale the color channels down through a table.
                var slice = session._frozen.Layout.ToBitmap(bounds);
                PInvoke.BitBlt(_dimDc, 0, 0, bounds.Width, bounds.Height, session._frozenDc, slice.Left, slice.Top, ROP_CODE.SRCCOPY);
                PInvoke.GdiFlush();
                var pixels = MemoryMarshal.Cast<byte, uint>(new Span<byte>(dimBits, _bufferBytes));
                for (var i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    pixels[i] = (p & 0xFF000000u)
                        | ((uint)DimTable[(p >> 16) & 0xFF] << 16)
                        | ((uint)DimTable[(p >> 8) & 0xFF] << 8)
                        | DimTable[p & 0xFF];
                }

                var logFont = new LOGFONTW
                {
                    lfHeight = -(int)Math.Round(12.0 * monitor.Dpi / 72.0),
                    lfWeight = 400,
                    lfCharSet = FONT_CHARSET.DEFAULT_CHARSET,
                    lfOutPrecision = FONT_OUTPUT_PRECISION.OUT_DEFAULT_PRECIS,
                    lfClipPrecision = FONT_CLIP_PRECISION.CLIP_DEFAULT_PRECIS,
                    lfQuality = FONT_QUALITY.CLEARTYPE_QUALITY,
                };
                "Segoe UI".AsSpan().CopyTo(logFont.lfFaceName.AsSpan());
                _font = PInvoke.CreateFontIndirect(&logFont);
                _ownsFont = !_font.IsNull;
                if (_font.IsNull)
                {
                    _font = new HFONT(PInvoke.GetStockObject(GET_STOCK_OBJECT_FLAGS.DEFAULT_GUI_FONT).Value);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public HWND Handle { get; private set; }

        public MonitorInfo Monitor { get; }

        /// <summary>Scales a 96-DPI design length to this monitor.</summary>
        private int S(int logical) => (int)Math.Round(logical * _scale);

        public void Paint()
        {
            var bounds = Monitor.Bounds;
            var layout = _session._frozen.Layout;
            var back = _backDc;

            PInvoke.BitBlt(back, 0, 0, bounds.Width, bounds.Height, _dimDc, 0, 0, ROP_CODE.SRCCOPY);

            var selection = _session._selection;
            var visible = bounds.Intersect(selection.Rect);
            if (!visible.IsEmpty)
            {
                var local = visible.Translate(-bounds.Left, -bounds.Top);
                var source = layout.ToBitmap(visible);
                PInvoke.BitBlt(back, local.Left, local.Top, local.Width, local.Height, _session._frozenDc, source.Left, source.Top, ROP_CODE.SRCCOPY);

                var previousPen = PInvoke.SelectObject(back, _session._accentPen);
                var previousBrush = PInvoke.SelectObject(back, PInvoke.GetStockObject(GET_STOCK_OBJECT_FLAGS.NULL_BRUSH));
                var frame = selection.Rect.Translate(-bounds.Left, -bounds.Top);
                PInvoke.Rectangle(back, frame.Left - 1, frame.Top - 1, frame.Right + 1, frame.Bottom + 1);
                PInvoke.SelectObject(back, previousBrush);
                PInvoke.SelectObject(back, previousPen);
            }

            var cursor = selection.Cursor;
            var onThisMonitor = bounds.Contains(cursor);
            if (onThisMonitor && _session._mode == OverlayMode.Region && selection.State != SelectionState.Selected)
            {
                DrawCrosshair(back, cursor.X - bounds.Left, cursor.Y - bounds.Top, bounds.Width, bounds.Height);
            }

            if (onThisMonitor)
            {
                var label = BuildLabel(selection);
                if (label.Length > 0)
                {
                    DrawLabel(back, label, cursor.X - bounds.Left + S(16), cursor.Y - bounds.Top + S(16), bounds);
                }

                if (_session._magnifier && _session._mode == OverlayMode.Region && selection.State != SelectionState.Selected)
                {
                    DrawMagnifier(back, cursor, bounds);
                }
            }

            PAINTSTRUCT paint;
            var hdc = PInvoke.BeginPaint(Handle, &paint);
            PInvoke.BitBlt(hdc, 0, 0, bounds.Width, bounds.Height, back, 0, 0, ROP_CODE.SRCCOPY);
            PInvoke.EndPaint(Handle, &paint);
        }

        private string BuildLabel(RegionSelection selection)
        {
            var options = _session._options;
            var parts = new List<string>(2);
            if (!selection.Rect.IsEmpty)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{selection.Rect.Width} x {selection.Rect.Height}"));
            }

            if (options.ShowCoordinates)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{selection.Cursor.X}, {selection.Cursor.Y}"));
            }

            return string.Join("   ", parts);
        }

        private void DrawCrosshair(HDC dc, int x, int y, int width, int height)
        {
            var previous = PInvoke.SelectObject(dc, _session._accentPen);
            PInvoke.MoveToEx(dc, 0, y);
            PInvoke.LineTo(dc, width, y);
            PInvoke.MoveToEx(dc, x, 0);
            PInvoke.LineTo(dc, x, height);
            PInvoke.SelectObject(dc, previous);
        }

        private void DrawLabel(HDC dc, string text, int x, int y, PixelRect bounds)
        {
            var previousFont = PInvoke.SelectObject(dc, _font);
            PInvoke.GetTextExtentPoint32W(dc, text, text.Length, out var size);
            var pad = S(6);
            var width = size.Width + (2 * pad);
            var height = size.Height + (2 * pad);
            if (x + width > bounds.Width)
            {
                x = Math.Max(0, x - width - S(32));
            }

            if (y + height > bounds.Height)
            {
                y = Math.Max(0, y - height - S(32));
            }

            var box = new RECT { left = x, top = y, right = x + width, bottom = y + height };
            PInvoke.FillRect(dc, &box, _session._labelBrush);
            PInvoke.SetBkMode(dc, BACKGROUND_MODE.TRANSPARENT);
            PInvoke.SetTextColor(dc, LabelText);
            PInvoke.TextOut(dc, x + pad, y + pad, text, text.Length);
            PInvoke.SelectObject(dc, previousFont);
        }

        private void DrawMagnifier(HDC dc, PixelPoint cursor, PixelRect bounds)
        {
            var layout = _session._frozen.Layout;
            var frozen = _session._frozen;
            const int Half = MagnifierSourcePixels / 2;
            var zoom = Math.Max(2, S(MagnifierZoom));
            var size = MagnifierSourcePixels * zoom;

            var sourceCenter = layout.ToBitmap(cursor);
            var source = frozen.Bounds.Intersect(new PixelRect(sourceCenter.X - Half, sourceCenter.Y - Half, MagnifierSourcePixels, MagnifierSourcePixels));
            if (source.IsEmpty)
            {
                return;
            }

            var x = cursor.X - bounds.Left + S(24);
            var y = cursor.Y - bounds.Top - size - S(24);
            if (y < 0)
            {
                y = cursor.Y - bounds.Top + S(48);
            }

            if (x + size > bounds.Width)
            {
                x = cursor.X - bounds.Left - size - S(24);
            }

            x = Math.Clamp(x, 0, Math.Max(0, bounds.Width - size));
            y = Math.Clamp(y, 0, Math.Max(0, bounds.Height - size));

            var destX = x + ((source.Left - (sourceCenter.X - Half)) * zoom);
            var destY = y + ((source.Top - (sourceCenter.Y - Half)) * zoom);

            var box = new RECT { left = x, top = y, right = x + size, bottom = y + size };
            PInvoke.FillRect(dc, &box, _session._labelBrush);
            PInvoke.SetStretchBltMode(dc, STRETCH_BLT_MODE.COLORONCOLOR);
            PInvoke.StretchBlt(dc, destX, destY, source.Width * zoom, source.Height * zoom, _session._frozenDc, source.Left, source.Top, source.Width, source.Height, ROP_CODE.SRCCOPY);

            var previous = PInvoke.SelectObject(dc, _session._accentPen);
            var previousBrush = PInvoke.SelectObject(dc, PInvoke.GetStockObject(GET_STOCK_OBJECT_FLAGS.NULL_BRUSH));
            PInvoke.Rectangle(dc, x, y, x + size, y + size);
            var center = Half * zoom;
            PInvoke.Rectangle(dc, x + center, y + center, x + center + zoom + 1, y + center + zoom + 1);
            PInvoke.SelectObject(dc, previousBrush);
            PInvoke.SelectObject(dc, previous);
        }

        private static HBITMAP CreateDib(int width, int height, out byte* bits)
        {
            var info = new BITMAPINFO();
            info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
            info.bmiHeader.biWidth = width;
            info.bmiHeader.biHeight = -height;
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            info.bmiHeader.biCompression = (uint)BI_COMPRESSION.BI_RGB;
            void* raw;
            var bitmap = PInvoke.CreateDIBSection(HDC.Null, &info, DIB_USAGE.DIB_RGB_COLORS, &raw, HANDLE.Null, 0);
            if (bitmap.IsNull || raw is null)
            {
                var error = Marshal.GetLastWin32Error();
                if (!bitmap.IsNull)
                {
                    PInvoke.DeleteObject(bitmap);
                }

                throw new Win32Exception(error, "CreateDIBSection failed for the overlay buffer.");
            }

            bits = (byte*)raw;
            return bitmap;
        }

        public void Dispose()
        {
            if (!Handle.IsNull)
            {
                windowsOnThread?.Remove((nint)Handle.Value);
                PInvoke.DestroyWindow(Handle);
                Handle = HWND.Null;
            }

            if (_ownsFont && !_font.IsNull)
            {
                PInvoke.DeleteObject(_font);
            }

            _font = HFONT.Null;
            _ownsFont = false;

            // The window is gone, so nothing paints any more. Let GDI finish, then zero both copies of the screen
            // before their bitmaps are deleted. Either pointer may be null when construction failed part-way.
            PInvoke.GdiFlush();
            if (_backBits is not null)
            {
                new Span<byte>(_backBits, _bufferBytes).Clear();
                _backBits = null;
            }

            if (_dimBits is not null)
            {
                new Span<byte>(_dimBits, _bufferBytes).Clear();
                _dimBits = null;
            }

            ReleaseBuffer(ref _backDc, ref _backBitmap, _backPrevious);
            ReleaseBuffer(ref _dimDc, ref _dimBitmap, _dimPrevious);
        }

        private static void ReleaseBuffer(ref HDC dc, ref HBITMAP bitmap, HGDIOBJ previous)
        {
            if (!dc.IsNull && !previous.IsNull)
            {
                PInvoke.SelectObject(dc, previous);
            }

            if (!bitmap.IsNull)
            {
                PInvoke.DeleteObject(bitmap);
                bitmap = HBITMAP.Null;
            }

            if (!dc.IsNull)
            {
                PInvoke.DeleteDC(dc);
                dc = HDC.Null;
            }
        }
    }
}
