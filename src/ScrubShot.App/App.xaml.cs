// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using H.NotifyIcon;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using ScrubShot.Capture;
using ScrubShot.Core.Logging;

namespace ScrubShot.App;

/// <summary>
/// Application entry point. ScrubShot has no main window; it lives in the tray.
/// </summary>
public sealed partial class App : Application
{
    // No log provider is wired yet. The opt-in local file sink arrives with the Advanced settings
    // tab (docs/decisions.md D-014). Until then every log call is a no-op.
    private readonly ILogger _logger = NullLogger.Instance;

    private TaskbarIcon? _trayIcon;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var isPerMonitorV2 = ProcessDpi.IsPerMonitorV2();
        Log.AppStarted(_logger, typeof(App).Assembly.GetName().Version ?? new Version(0, 0), isPerMonitorV2);
        if (!isPerMonitorV2)
        {
            Log.DpiAwarenessUnexpected(_logger);
        }

        var exitCommand = (XamlUICommand)Resources["ExitCommand"];
        exitCommand.ExecuteRequested += (_, _) => Shutdown(0);

        _trayIcon = (TaskbarIcon)Resources["TrayIcon"];
        _trayIcon.ForceCreate();
    }

    private void Shutdown(int exitCode)
    {
        Log.AppExiting(_logger, exitCode);

        _trayIcon?.Dispose();
        _trayIcon = null;

        // There is no window whose close would end the dispatcher loop, so end the process
        // explicitly. This matches the library's windowless sample (H.NotifyIcon issue 66).
        Environment.Exit(exitCode);
    }
}
