// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Diagnostics;
using System.Globalization;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ScrubShot.App.Settings;
using ScrubShot.Capture;
using ScrubShot.Capture.Coordination;
using ScrubShot.Capture.Hotkeys;
using ScrubShot.Core.Coordination;
using ScrubShot.Core.Hotkeys;
using ScrubShot.Core.Lifecycle;
using ScrubShot.Core.Logging;
using ScrubShot.Core.Output;
using ScrubShot.Core.Settings;
using ScrubShot.Output;

namespace ScrubShot.App;

/// <summary>
/// Application entry point. ScrubShot has no main window; it lives in the tray.
/// </summary>
public sealed partial class App : Application, ISettingsHost
{
    private const int TrayIconAttempts = 5;
    private static readonly TimeSpan TrayIconRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExitWaitForOutput = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExitWaitForRollback = TimeSpan.FromSeconds(1);

    // No log provider is wired yet. The opt-in local file sink arrives with the Advanced settings
    // tab (docs/decisions.md D-014). Until then every log call is a no-op.
    private readonly ILogger _logger = NullLogger.Instance;

    private readonly SettingsService _settings;

    // Cancelled at exit so an output that is still running rolls back (the saver deletes a partial file).
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Lock _inFlightLock = new();
    private readonly HashSet<Task> _inFlight = [];

    private SingleInstance? _instance;
    private TaskbarIcon? _trayIcon;
    private MenuFlyoutSubItem? _monitorMenu;
    private MenuFlyoutItem? _openLastItem;
    private DispatcherQueue? _dispatcher;
    private OutputSink? _output;
    private HotkeyService? _hotkeys;
    private IReadOnlyList<HotkeyBinding> _hotkeyBindings = [];
    private HotkeySettings? _appliedHotkeys;
    private Dictionary<HotkeyAction, SettingSource>? _appliedHotkeySources;
    private CaptureCoordinator? _coordinator;
    private SettingsWindow? _settingsWindow;

    public App()
    {
        InitializeComponent();
        _settings = new SettingsService(
            new JsonSettingsStore(JsonSettingsStore.DefaultFilePath, _logger),
            RegistryPolicySource.Machine(),
            RegistryPolicySource.User(),
            SaveFolder.PicturesFolder,
            _logger);
    }

    EffectiveSettings ISettingsHost.Effective => _settings.Effective;

    IReadOnlyList<HotkeyBinding> ISettingsHost.HotkeyBindings => _hotkeyBindings;

    string? ISettingsHost.PicturesFolder => SaveFolder.PicturesFolder();

    SettingsChangeResult ISettingsHost.TryChange(ScrubShotSettings proposed) => _settings.TryChange(proposed);

    void ISettingsHost.RefreshPolicy() => _settings.RefreshPolicy();

    void ISettingsHost.FixSnippingToolConflict()
    {
        if (_hotkeys is null)
        {
            return;
        }

        try
        {
            // Only ever called from the button the user pressed in Settings (D-013).
            _hotkeyBindings = _hotkeys.FixSnippingToolConflict();
        }
        catch (Exception ex) when (ex is TimeoutException or ObjectDisposedException)
        {
            Log.HotkeyServiceFailed(_logger, ErrorCodes.HResultOf(ex));
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // A second launch passes on its right to take the foreground, signals the running instance and exits (D-041).
        _instance = SingleInstance.TryAcquire(beforeSignal: () => ForegroundPermission.AllowAnyProcess());
        if (_instance is null)
        {
            Log.SecondInstanceExiting(_logger);
            Environment.Exit(0);
            return;
        }

        // A tray app outlives its windows. Without this, closing the Settings window (the last XAML window) would
        // end the message loop and with it the app (Learn: Application.DispatcherShutdownMode).
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _instance.ActivationRequested += () =>
        {
            Log.ActivationRequested(_logger);
            _dispatcher.TryEnqueue(ShowSettings);
        };
        _instance.Start();

        var isPerMonitorV2 = ProcessDpi.IsPerMonitorV2();
        Log.AppStarted(_logger, typeof(App).Assembly.GetName().Version ?? new Version(0, 0), isPerMonitorV2);
        if (!isPerMonitorV2)
        {
            Log.DpiAwarenessUnexpected(_logger);
        }

        // Nothing in a settings file or policy key may stop the app from starting; on a failure defaults stay in effect.
        _settings.Reload();
        _settings.Changed += OnSettingsChanged;
        SyncStartupRegistration();

        _output = new OutputSink(ClipboardWriter.Instance, CaptureSaver.Instance, () => _settings.Effective.Values.Output, _logger);
        _output.Completed += result => _dispatcher.TryEnqueue(() => Notify(result));
        _coordinator = new CaptureCoordinator(
            new GdiScreenCapturer(),
            MonitorEnumerator.GetLayout,
            OverlayRegionSelector.Instance,
            _output,
            () => _settings.Effective.Values.Capture,
            _logger);

        _trayIcon = (TaskbarIcon)Resources["TrayIcon"];
        _trayIcon.ContextFlyout = BuildTrayMenu();
        // H.NotifyIcon 2.4.1 (TaskbarIcon.MouseEvents.OnMouseEvent) executes RightClickCommand in the IconRightMouseUp case
        // before ShowContextMenu, and PopupMenu mode rebuilds the native menu from the flyout items on every show.
        // WM_CONTEXTMENU is routed as a keyboard event there and never shows the menu. Re-check on a library upgrade.
        var refresh = new XamlUICommand();
        refresh.ExecuteRequested += (_, _) => RefreshDynamicMenuItems();
        _trayIcon.RightClickCommand = refresh;
        CreateTrayIcon(attempt: 1);

        StartHotkeys();
    }

    /// <summary>
    /// Adds the icon to the notification area. The shell can refuse while Explorer is starting or restarting, and
    /// the library only adds it again when Explorer announces a new taskbar, so a failure is retried a few times
    /// instead of leaving the app running with no icon.
    /// </summary>
    private void CreateTrayIcon(int attempt)
    {
        if (_trayIcon is not { IsCreated: false } trayIcon)
        {
            return;
        }

        try
        {
            // Not the library default: with efficiency mode on, H.NotifyIcon 2.4.1 puts the whole process in the
            // idle priority class under EcoQoS for its lifetime, so a capture would wait behind any busy app
            // (D-049). Re-check on a library upgrade.
            trayIcon.ForceCreate(enablesEfficiencyMode: false);
        }
        catch (InvalidOperationException ex)
        {
            Log.TrayIconFailed(_logger, attempt, ErrorCodes.HResultOf(ex));
            if (attempt < TrayIconAttempts && _dispatcher is { } dispatcher)
            {
                var timer = dispatcher.CreateTimer();
                timer.Interval = TrayIconRetryDelay;
                timer.IsRepeating = false;
                timer.Tick += (_, _) => CreateTrayIcon(attempt + 1);
                timer.Start();
            }
        }
    }

    /// <summary>Keeps the Run key in step with the effective setting (policy included) at every launch (D-042).</summary>
    private void SyncStartupRegistration()
    {
        if (Environment.ProcessPath is { } exe)
        {
            StartupRegistration.Sync(_settings.Effective.Values.General.LaunchAtStartup, exe, new RegistryStartupStore(), _logger);
        }
    }

    /// <summary>
    /// The settings in effect changed: the user saved something, or a reload or policy refresh found a difference.
    /// Startup registration and hotkeys follow at once, without a restart. A policy refresh raises this from the
    /// capture's background thread, so the work is moved to the UI thread first.
    /// </summary>
    private void OnSettingsChanged(EffectiveSettings effective)
    {
        if (_dispatcher is { HasThreadAccess: false } dispatcher)
        {
            dispatcher.TryEnqueue(() => OnSettingsChanged(effective));
            return;
        }

        SyncStartupRegistration();
        ApplyHotkeys();
        _settingsWindow?.ViewModel.OnSettingsChangedElsewhere();
    }

    /// <summary>Opens the Settings window, or brings the open one forward. UI thread only.</summary>
    private void ShowSettings()
    {
        // Policy and hand edits to the file made since launch are picked up before the window shows them.
        _settings.Reload();

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(new SettingsViewModel(this));
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        // Activate restores and focuses the window; SetForegroundWindow is what raises it above other applications,
        // using the right a second launch passed on (D-046).
        _settingsWindow.Activate();
        ForegroundPermission.BringToForeground(WinRT.Interop.WindowNative.GetWindowHandle(_settingsWindow));
    }

    private MenuFlyout BuildTrayMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Item("Capture region", () => Capture(CaptureRequest.Region)));
        menu.Items.Add(Item("Capture window", () => Capture(CaptureRequest.Window)));
        menu.Items.Add(Item("Capture full screen", () => Capture(CaptureRequest.FullScreen)));
        _monitorMenu = new MenuFlyoutSubItem { Text = "Capture monitor" };
        menu.Items.Add(_monitorMenu);
        menu.Items.Add(Item("Repeat last region", () => Capture(CaptureRequest.RepeatLastRegion)));
        menu.Items.Add(new MenuFlyoutSeparator());
        _openLastItem = Item("Open last capture", OpenLastCapture);
        _openLastItem.IsEnabled = false;
        menu.Items.Add(_openLastItem);
        menu.Items.Add(Item("Settings", ShowSettings));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Exit", () => Shutdown(0)));
        RefreshDynamicMenuItems();
        return menu;
    }

    /// <summary>Rebuilds the per-monitor submenu from the live layout and enables Open last capture; the tray library reads the items each time the menu opens.</summary>
    private void RefreshDynamicMenuItems()
    {
        if (_openLastItem is { } openLast)
        {
            openLast.IsEnabled = _output?.HasLastCapture == true;
        }

        if (_monitorMenu is null)
        {
            return;
        }

        _monitorMenu.Items.Clear();
        try
        {
            var layout = MonitorEnumerator.GetLayout();
            foreach (var monitor in layout.Monitors)
            {
                var index = monitor.Index;
                var label = string.Create(CultureInfo.InvariantCulture, $"Monitor {index + 1}: {monitor.Bounds.Width}x{monitor.Bounds.Height}{(monitor.IsPrimary ? " (primary)" : string.Empty)}");
                _monitorMenu.Items.Add(Item(label, () => Capture(CaptureRequest.ForMonitor(index))));
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        {
            Log.CaptureFailed(_logger, CaptureMode.Monitor, ErrorCodes.HResultOf(ex));
        }

        _monitorMenu.IsEnabled = _monitorMenu.Items.Count > 0;
    }

    /// <summary>
    /// Tray balloon for the outcome (D-043). The text names the folder, never the capture content. The shell call
    /// can fail (Explorer restarting, icon not added yet) and the library throws when it does; this runs in a
    /// dispatcher callback, where an escaped exception ends the process, so it is caught here.
    /// </summary>
    private void Notify(OutputResult result)
    {
        if (_trayIcon is not { IsCreated: true } trayIcon)
        {
            return;
        }

        try
        {
            trayIcon.ShowNotification(
                title: "ScrubShot",
                message: OutputMessages.For(result),
                icon: result.AnyFailed ? NotificationIcon.Warning : NotificationIcon.Info);
        }
        catch (InvalidOperationException ex)
        {
            Log.OutputFailed(_logger, OutputStage.Notification, ErrorCodes.HResultOf(ex));
        }
    }

    /// <summary>Opens the last saved file with its default app, or re-copies the last in-memory capture when nothing was saved.</summary>
    private void OpenLastCapture()
    {
        if (_output is not { } output || output.LastResult is not { } last)
        {
            return;
        }

        if (last.SavedPath is { } path && File.Exists(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                Log.OutputFailed(_logger, OutputStage.File, ErrorCodes.HResultOf(ex));
                return;
            }
        }

        var token = _lifetime.Token;
        Track(Task.Run(async () =>
        {
            var recopied = false;
            try
            {
                recopied = await output.RecopyLastAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.OutputFailed(_logger, OutputStage.Clipboard, ErrorCodes.HResultOf(ex));
            }

            if (last.SavedPath is not null)
            {
                Log.LastCaptureFileMissing(_logger, recopied);
            }

            if (recopied)
            {
                _dispatcher?.TryEnqueue(() => Notify(new OutputResult(true, null, last.ExcludedFromHistory)));
            }
        }));
    }

    private static MenuFlyoutItem Item(string text, Action action)
    {
        // The tray library's native popup mode dispatches through Command, not Click.
        var command = new XamlUICommand { Label = text };
        command.ExecuteRequested += (_, _) => action();
        return new MenuFlyoutItem { Text = text, Command = command };
    }

    /// <summary>
    /// Runs a capture off the caller's thread (tray UI thread or hotkey thread); the coordinator reports the outcome
    /// through the log. Policy is read again first, so a policy pushed while the app runs holds from this capture on.
    /// </summary>
    private void Capture(CaptureRequest request)
    {
        if (_coordinator is not { } coordinator || _lifetime.IsCancellationRequested)
        {
            return;
        }

        var token = _lifetime.Token;
        Track(Task.Run(async () =>
        {
            try
            {
                _settings.RefreshPolicy();
            }
            catch (Exception ex)
            {
                // A failing subscriber must not cost the user the capture; the settings in effect are still valid.
                Log.SettingsLoadFailed(_logger, ErrorCodes.HResultOf(ex));
            }

            await coordinator.CaptureAsync(request, token).ConfigureAwait(false);
        }));
    }

    /// <summary>Remembers work that Exit should wait for briefly: a capture or re-copy that is writing its output.</summary>
    private void Track(Task task)
    {
        lock (_inFlightLock)
        {
            _inFlight.Add(task);
        }

        task.ContinueWith(
            finished =>
            {
                lock (_inFlightLock)
                {
                    _inFlight.Remove(finished);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Registers the configured hotkeys and routes presses to the coordinator.</summary>
    private void StartHotkeys()
    {
        try
        {
            _hotkeys = HotkeyService.Start();
            _hotkeys.Pressed += action =>
            {
                Log.HotkeyPressed(_logger, action);
                Capture(CaptureRequest.FromHotkey(action));
            };
            _hotkeys.HandlerFailed += ex => Log.HotkeyHandlerFailed(_logger, ErrorCodes.HResultOf(ex));
            ApplyHotkeys();
        }
        catch (InvalidOperationException ex)
        {
            Log.HotkeyServiceFailed(_logger, ErrorCodes.HResultOf(ex));
        }
    }

    /// <summary>
    /// Registers the hotkeys now in effect, unless they and their sources are the ones already applied. The sources
    /// matter: a hotkey that policy sets keeps its gesture when another action asks for the same one.
    /// </summary>
    private void ApplyHotkeys()
    {
        if (_hotkeys is null)
        {
            return;
        }

        var effective = _settings.Effective;
        var wanted = effective.Values.Hotkeys;
        var sources = Enum.GetValues<HotkeyAction>().ToDictionary(action => action, action => effective.SourceOf(SettingsValidator.HotkeyPath(action)));
        if (wanted == _appliedHotkeys && _appliedHotkeySources is { } applied && sources.All(pair => applied.TryGetValue(pair.Key, out var source) && source == pair.Value))
        {
            return;
        }

        try
        {
            var bindings = _hotkeys.Apply(wanted, sources);
            _appliedHotkeys = wanted;
            _appliedHotkeySources = sources;
            _hotkeyBindings = bindings;
            Log.HotkeysApplied(_logger, bindings.Count(b => b.IsRegistered), bindings.Count(b => !b.IsRegistered));
            foreach (var binding in bindings.Where(b => !b.IsRegistered))
            {
                Log.HotkeyConflict(_logger, binding.Action, binding.Conflict, binding.ErrorCode);
            }
        }
        catch (Exception ex) when (ex is TimeoutException or ObjectDisposedException)
        {
            Log.HotkeyServiceFailed(_logger, ErrorCodes.HResultOf(ex));
        }
    }

    private void Shutdown(int exitCode)
    {
        Log.AppExiting(_logger, exitCode);

        _hotkeys?.Dispose(); // first, so no new capture starts
        _hotkeys = null;
        FinishInFlightOutput();
        _output?.Dispose(); // clears the last in-memory capture
        _output = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _instance?.Dispose();
        _instance = null;

        // There is no window whose close would end the dispatcher loop, so end the process
        // explicitly. This matches the library's windowless sample (H.NotifyIcon issue 66).
        Environment.Exit(exitCode);
    }

    /// <summary>
    /// Environment.Exit does not unwind, so a save that is mid-write would leave a truncated file. A capture that
    /// is already writing gets a moment to finish (the user asked for that file); after that it is cancelled, which
    /// makes the saver delete what it started, and gets a moment to do so. Bounded, so Exit always exits.
    /// </summary>
    private void FinishInFlightOutput()
    {
        Task[] pending;
        lock (_inFlightLock)
        {
            pending = [.. _inFlight];
        }

        try
        {
            if (pending.Length > 0 && !Task.WaitAll(pending, ExitWaitForOutput))
            {
                _lifetime.Cancel();
                Task.WaitAll(pending, ExitWaitForRollback);
            }
        }
        catch (AggregateException)
        {
            // A failed capture is already logged by the coordinator; exit continues.
        }

        _lifetime.Cancel();
    }
}
