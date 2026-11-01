// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using ScrubShot.Core.Hotkeys;
using ScrubShot.Core.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ScrubShot.Capture.Hotkeys;

/// <summary>
/// Registers the global hotkeys on a dedicated thread that owns a message-only window, reports conflicts
/// per action, and raises <see cref="Pressed"/> when one fires. Settings changes are applied with
/// <see cref="Apply"/>, which unregisters everything and registers again, so no restart is needed (D-013, D-039).
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const string ClassName = "ScrubShotHotkeys";
    private const uint WorkMessage = PInvoke.WM_APP + 1;
    private const ushort F12 = 0x7B;
    private const int ErrorClassAlreadyExists = 1410;

    private static readonly TimeSpan InvokeTimeout = TimeSpan.FromSeconds(10);
    private static readonly WNDPROC WindowProcedure = WndProc;
    private static readonly Lock ClassLock = new();
    private static bool classRegistered;

    [ThreadStatic]
    private static HotkeyService? serviceOnThread;

    private readonly ConcurrentQueue<Action> _work = new();
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private readonly Lock _bindingsLock = new();
    private readonly Dictionary<HotkeyAction, HotkeyGesture> _registered = [];
    private HWND _window;
    private IReadOnlyList<HotkeyBinding> _bindings = [];
    private HotkeySettings? _lastSettings;
    private Exception? _startupError;
    private volatile bool _disposed;

    private HotkeyService()
    {
        _thread = new Thread(Run) { Name = "ScrubShot hotkeys", IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    /// <summary>
    /// Raised on the hotkey thread. Handlers must marshal to their own thread and return quickly; they must not
    /// call <see cref="Dispose"/> synchronously from inside the handler.
    /// </summary>
    public event Action<HotkeyAction>? Pressed;

    /// <summary>Raised on the hotkey thread when a <see cref="Pressed"/> handler threw. The exception never reaches native frames.</summary>
    public event Action<Exception>? HandlerFailed;

    /// <summary>The result of the last <see cref="Apply"/>: one entry per action.</summary>
    public IReadOnlyList<HotkeyBinding> Bindings
    {
        get
        {
            lock (_bindingsLock)
            {
                return _bindings;
            }
        }
    }

    /// <summary>Starts the hotkey thread and its hidden window. Nothing is registered until <see cref="Apply"/>.</summary>
    public static HotkeyService Start()
    {
        var service = new HotkeyService();
        service._thread.Start();
        service._ready.Wait();
        if (service._startupError is { } error)
        {
            throw new InvalidOperationException("The hotkey service could not start.", error);
        }

        return service;
    }

    /// <summary>Replaces every registration with the hotkeys in <paramref name="settings"/> and returns the outcome per action.</summary>
    public IReadOnlyList<HotkeyBinding> Apply(HotkeySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _lastSettings = settings;
        return Invoke(() => ApplyOnThread(settings));
    }

    /// <summary>
    /// Turns off the Windows switch that routes Print Screen to the Snipping Tool, then registers again.
    /// Call only after the user clicked the fix; it changes a per-user Windows setting (D-013).
    /// </summary>
    public IReadOnlyList<HotkeyBinding> FixSnippingToolConflict()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!SnippingToolPrintScreen.Disable())
        {
            return Bindings;
        }

        return _lastSettings is { } settings ? Apply(settings) : Bindings;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_thread.IsAlive)
        {
            _ready.Dispose();
            return;
        }

        var onOwnThread = Thread.CurrentThread == _thread;
        try
        {
            Post(ShutdownOnThread);
        }
        catch (ObjectDisposedException)
        {
            // The window is already gone; the thread is finishing on its own.
        }

        if (!onOwnThread)
        {
            _thread.Join(TimeSpan.FromSeconds(5));
            _ready.Dispose();
        }
    }

    private void ShutdownOnThread()
    {
        UnregisterAll();
        PInvoke.DestroyWindow(_window);
        _window = HWND.Null;
        PInvoke.PostQuitMessage(0);
    }

    private T Invoke<T>(Func<T> work)
    {
        if (Thread.CurrentThread == _thread)
        {
            return work(); // already on the hotkey thread; waiting on it would deadlock
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        if (!completion.Task.Wait(InvokeTimeout))
        {
            throw new TimeoutException("The hotkey thread did not respond.");
        }

        return completion.Task.GetAwaiter().GetResult();
    }

    private void Post(Action work)
    {
        _work.Enqueue(work);
        var window = _window;
        if (!_thread.IsAlive || window.IsNull || !PInvoke.PostMessage(window, WorkMessage, default, default))
        {
            throw new ObjectDisposedException(nameof(HotkeyService), "The hotkey window is no longer available.");
        }
    }

    private unsafe void Run()
    {
        serviceOnThread = this;
        try
        {
            EnsureClassRegistered();
            _window = PInvoke.CreateWindowEx(0, ClassName, null, 0, 0, 0, 0, 0, HWND.HWND_MESSAGE, null, null, null);
            if (_window.IsNull)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx failed for the hotkey window.");
            }
        }
        catch (Exception ex)
        {
            _startupError = ex;
            _ready.Set();
            return;
        }

        _ready.Set();

        MSG message;
        while (PInvoke.GetMessage(&message, HWND.Null, 0, 0))
        {
            PInvoke.TranslateMessage(&message);
            PInvoke.DispatchMessage(&message);
        }
    }

    private IReadOnlyList<HotkeyBinding> ApplyOnThread(HotkeySettings settings)
    {
        UnregisterAll();
        var snippingToolOwnsPrintScreen = SnippingToolPrintScreen.IsEnabled();
        var results = new List<HotkeyBinding>(4);

        foreach (var (action, text) in HotkeyBindings.FromSettings(settings))
        {
            if (!HotkeyGesture.TryParse(text, out var gesture))
            {
                results.Add(new HotkeyBinding(action, text, null, HotkeyConflict.InvalidGesture));
                continue;
            }

            if (gesture.VirtualKey == F12)
            {
                // Reserved for the debugger at all times (Learn: RegisterHotKey remarks).
                results.Add(new HotkeyBinding(action, text, gesture, HotkeyConflict.ReservedKey));
                continue;
            }

            if (!gesture.IsRegistrable)
            {
                results.Add(new HotkeyBinding(action, text, gesture, HotkeyConflict.NeedsModifier));
                continue;
            }

            if (_registered.ContainsValue(gesture))
            {
                results.Add(new HotkeyBinding(action, text, gesture, HotkeyConflict.DuplicateInSettings));
                continue;
            }

            var modifiers = (HOT_KEY_MODIFIERS)(uint)gesture.Modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
            if (PInvoke.RegisterHotKey(_window, (int)action, modifiers, gesture.VirtualKey))
            {
                _registered[action] = gesture;
                var conflict = snippingToolOwnsPrintScreen && SnippingToolPrintScreen.Claims(gesture)
                    ? HotkeyConflict.OwnedBySnippingTool   // Windows still routes the key to the Snipping Tool
                    : HotkeyConflict.None;
                results.Add(new HotkeyBinding(action, text, gesture, conflict));
                continue;
            }

            var error = Marshal.GetLastWin32Error();
            var reason = error == (int)WIN32_ERROR.ERROR_HOTKEY_ALREADY_REGISTERED
                ? snippingToolOwnsPrintScreen && SnippingToolPrintScreen.Claims(gesture) ? HotkeyConflict.OwnedBySnippingTool : HotkeyConflict.OwnedByAnotherApp
                : HotkeyConflict.Failed;
            results.Add(new HotkeyBinding(action, text, gesture, reason, error));
        }

        lock (_bindingsLock)
        {
            _bindings = results;
        }

        return results;
    }

    private void UnregisterAll()
    {
        foreach (var action in _registered.Keys.ToList())
        {
            PInvoke.UnregisterHotKey(_window, (int)action);
        }

        _registered.Clear();
    }

    private LRESULT HandleMessage(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case WorkMessage:
                while (_work.TryDequeue(out var work))
                {
                    Guarded(work);
                }

                return new LRESULT(0);

            case PInvoke.WM_HOTKEY:
                var id = (int)(nuint)wParam.Value;
                if (Enum.IsDefined(typeof(HotkeyAction), id))
                {
                    var action = (HotkeyAction)id;
                    Guarded(() => Pressed?.Invoke(action));
                }

                return new LRESULT(0);

            default:
                return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
        }
    }

    /// <summary>Runs managed code inside the window procedure. An exception must never unwind into the native caller.</summary>
    private void Guarded(Action work)
    {
        try
        {
            work();
        }
        catch (Exception ex)
        {
            try
            {
                HandlerFailed?.Invoke(ex);
            }
            catch (Exception)
            {
                // A failing failure handler is dropped; there is nowhere left to report it.
            }
        }
    }

    private static LRESULT WndProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam) =>
        serviceOnThread is { } service
            ? service.HandleMessage(hwnd, message, wParam, lParam)
            : PInvoke.DefWindowProc(hwnd, message, wParam, lParam);

    private static unsafe void EnsureClassRegistered()
    {
        lock (ClassLock)
        {
            if (classRegistered)
            {
                return;
            }

            var module = PInvoke.GetModuleHandle(default(PCWSTR));
            ushort atom;
            fixed (char* name = ClassName)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = WindowProcedure,
                    hInstance = new HINSTANCE(module.Value),
                    lpszClassName = new PCWSTR(name),
                };
                atom = PInvoke.RegisterClassEx(in wc);
            }

            if (atom == 0 && Marshal.GetLastWin32Error() != ErrorClassAlreadyExists)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx failed for the hotkey window.");
            }

            classRegistered = true;
        }
    }
}
