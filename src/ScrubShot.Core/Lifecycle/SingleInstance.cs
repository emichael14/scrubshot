// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

namespace ScrubShot.Core.Lifecycle;

/// <summary>
/// Guarantees one running ScrubShot per user session with a named mutex, and lets a second launch wake the first
/// through a named event (D-041). Both names use the <c>Local\</c> prefix, so they are scoped to the current
/// terminal-server session and never cross users or sessions.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string DefaultName = "ScrubShot";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread _watcher;
    private readonly int _ownerThreadId;
    private bool _started;
    private bool _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle activate)
    {
        _mutex = mutex;
        _activate = activate;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _watcher = new Thread(Watch) { Name = "ScrubShot activation", IsBackground = true };
    }

    /// <summary>Raised on a background thread when a second launch asked this instance to come forward. Marshal to the UI thread.</summary>
    public event Action? ActivationRequested;

    /// <summary>
    /// Takes the instance mutex. Returns null when another instance already holds it; in that case the other instance
    /// has been signalled and the caller should exit. Dispose the result on the same thread that called this.
    /// Subscribe to <see cref="ActivationRequested"/> and then call <see cref="Start"/>; a signal that arrives before
    /// that is kept by the event and delivered on start.
    /// </summary>
    public static SingleInstance? TryAcquire(string name = DefaultName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // The event is created before the mutex is taken, so whenever any instance owns the mutex the event
        // exists for a later launch to signal. The named constructor opens the existing event when there is one.
        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, EventName(name));
        var mutex = new Mutex(initiallyOwned: false, MutexName(name));
        bool owned;
        try
        {
            owned = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            owned = true; // the previous instance died without releasing; the mutex is ours now
        }

        if (!owned)
        {
            mutex.Dispose();
            using (activate)
            {
                activate.Set();
            }

            return null;
        }

        return new SingleInstance(mutex, activate);
    }

    /// <summary>Starts delivering <see cref="ActivationRequested"/>. Idempotent.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _started = true;
        _watcher.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop.Set();
        if (_started && Environment.CurrentManagedThreadId != _watcher.ManagedThreadId)
        {
            _watcher.Join(TimeSpan.FromSeconds(2));
        }

        if (Environment.CurrentManagedThreadId == _ownerThreadId)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned any more; closing the handle below is enough.
            }
        }

        _mutex.Dispose();
        _activate.Dispose();
        _stop.Dispose();
    }

    private static string MutexName(string name) => $@"Local\{name}.Instance";

    private static string EventName(string name) => $@"Local\{name}.Activate";

    private void Watch()
    {
        WaitHandle[] handles = [_activate, _stop];
        while (true)
        {
            int signalled;
            try
            {
                signalled = WaitHandle.WaitAny(handles);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (signalled != 0)
            {
                return;
            }

            try
            {
                ActivationRequested?.Invoke();
            }
            catch (Exception)
            {
                // A failing handler must not kill the watcher; the next activation is still delivered.
            }
        }
    }
}
