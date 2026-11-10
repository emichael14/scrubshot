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
using ScrubShot.Capture;
using ScrubShot.Capture.Coordination;
using ScrubShot.Capture.Hotkeys;
using ScrubShot.Core.Coordination;
using ScrubShot.Core.Lifecycle;
using ScrubShot.Core.Logging;
using ScrubShot.Core.Settings;
using ScrubShot.Output;

namespace ScrubShot.App;

/// <summary>
/// Application entry point. ScrubShot has no main window; it lives in the tray.
/// </summary>
public sealed partial class App : Application
{
    // No log provider is wired yet. The opt-in local file sink arrives with the Advanced settings
    // tab (docs/decisions.md D-014). Until then every log call is a no-op.
    private readonly ILogger _logger = NullLogger.Instance;

    private readonly ISettingsStore _settingsStore = new JsonSettingsStore(JsonSettingsStore.DefaultFilePath);

    private SingleInstance? _instance;
    private TaskbarIcon? _trayIcon;
    private MenuFlyoutSubItem? _monitorMenu;
    private MenuFlyoutItem? _openLastItem;
    private DispatcherQueue? _dispatcher;
    private OutputSink? _output;
    private HotkeyService? _hotkeys;
    private CaptureCoordinator? _coordinator;

    public App()
    {
        InitializeComponent();
    }

    /// <summary>Settings after policy, resolved at launch. Re-resolved when settings change (Milestone 1 step 8).</summary>
    public EffectiveSettings Settings { get; private set; } = SettingsResolver.Resolve(ScrubShotSettings.Defaults, InMemoryPolicySource.Empty, InMemoryPolicySource.Empty);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _instance = SingleInstance.TryAcquire();
        if (_instance is null)
        {
            // The running instance was signalled (D-040); this process has nothing else to do.
            Log.SecondInstanceExiting(_logger);
            Environment.Exit(0);
            return;
        }

        _instance.ActivationRequested += () => Log.ActivationRequested(_logger); // the Settings window opens here from step 8
        _instance.Start();

        var isPerMonitorV2 = ProcessDpi.IsPerMonitorV2();
        Log.AppStarted(_logger, typeof(App).Assembly.GetName().Version ?? new Version(0, 0), isPerMonitorV2);
        if (!isPerMonitorV2)
        {
            Log.DpiAwarenessUnexpected(_logger);
        }

        try
        {
            Settings = SettingsResolver.Resolve(
                _settingsStore.Load(),
                RegistryPolicySource.Machine(_logger),
                RegistryPolicySource.User(_logger),
                _logger);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
        {
            // Nothing in a settings file or policy key may stop the app from starting. Defaults stay in effect.
            Log.SettingsLoadFailed(_logger, ex.HResult);
        }

        SyncStartupRegistration();

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _output = new OutputSink(ClipboardWriter.Instance, CaptureSaver.Instance, () => Settings.Values.Output, _logger);
        _output.Completed += result => _dispatcher.TryEnqueue(() => Notify(result));
        _coordinator = new CaptureCoordinator(
            new GdiScreenCapturer(),
            MonitorEnumerator.GetLayout,
            OverlayRegionSelector.Instance,
            _output,
            () => Settings.Values.Capture,
            _logger);

        _trayIcon = (TaskbarIcon)Resources["TrayIcon"];
        _trayIcon.ContextFlyout = BuildTrayMenu();
        // H.NotifyIcon 2.4.1 (TaskbarIcon.MouseEvents.OnMouseEvent) executes RightClickCommand in the IconRightMouseUp case
        // before ShowContextMenu, and PopupMenu mode rebuilds the native menu from the flyout items on every show.
        // WM_CONTEXTMENU is routed as a keyboard event there and never shows the menu. Re-check on a library upgrade.
        var refresh = new XamlUICommand();
        refresh.ExecuteRequested += (_, _) => RefreshDynamicMenuItems();
        _trayIcon.RightClickCommand = refresh;
        _trayIcon.ForceCreate();

        StartHotkeys();
    }

    /// <summary>Keeps the Run key in step with the effective setting (policy included) at every launch (D-041).</summary>
    private void SyncStartupRegistration()
    {
        if (Environment.ProcessPath is { } exe)
        {
            StartupRegistration.Sync(Settings.Values.General.LaunchAtStartup, exe, new RegistryStartupStore(), _logger);
        }
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
        menu.Items.Add(new MenuFlyoutItem { Text = "Settings", IsEnabled = false }); // step 8
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

    /// <summary>Tray balloon for the outcome (D-043). The text names the folder, never the capture content.</summary>
    private void Notify(OutputResult result)
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.ShowNotification(
            title: "ScrubShot",
            message: OutputMessages.For(result),
            icon: result.AnyFailed ? NotificationIcon.Warning : NotificationIcon.Info);
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

        _ = Task.Run(async () =>
        {
            var recopied = false;
            try
            {
                recopied = await output.RecopyLastAsync().ConfigureAwait(false);
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
        });
    }

    private static MenuFlyoutItem Item(string text, Action action)
    {
        // The tray library's native popup mode dispatches through Command, not Click.
        var command = new XamlUICommand { Label = text };
        command.ExecuteRequested += (_, _) => action();
        return new MenuFlyoutItem { Text = text, Command = command };
    }

    /// <summary>Runs a capture off the caller's thread (tray UI thread or hotkey thread); the coordinator reports the outcome through the log.</summary>
    private void Capture(CaptureRequest request)
    {
        if (_coordinator is { } coordinator)
        {
            _ = Task.Run(() => coordinator.CaptureAsync(request));
        }
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
            var bindings = _hotkeys.Apply(Settings.Values.Hotkeys);
            Log.HotkeysApplied(_logger, bindings.Count(b => b.IsRegistered), bindings.Count(b => !b.IsRegistered));
            foreach (var binding in bindings.Where(b => !b.IsRegistered))
            {
                Log.HotkeyConflict(_logger, binding.Action, binding.Conflict, binding.ErrorCode);
            }
        }
        catch (InvalidOperationException ex)
        {
            Log.HotkeyServiceFailed(_logger, ErrorCodes.HResultOf(ex));
        }
    }

    private void Shutdown(int exitCode)
    {
        Log.AppExiting(_logger, exitCode);

        _hotkeys?.Dispose();
        _hotkeys = null;
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
}
