// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.Storage.Pickers;
using ScrubShot.Core.Settings;
using Windows.Graphics;
using Windows.System;

namespace ScrubShot.App.Settings;

/// <summary>
/// The minimal Settings window for Milestone 1: General, Hotkeys and Capture (D-046). All state and rules live in
/// <see cref="SettingsViewModel"/> and the settings service; this class only sizes the window, switches pages and
/// forwards two gestures (Browse, Enter in a text box).
/// </summary>
public sealed partial class SettingsWindow : Window
{
    // Sizes in device-independent pixels; scaled by the monitor's DPI once the content is loaded.
    private const double DefaultWidth = 960;
    private const double DefaultHeight = 720;
    private const double MinimumWidth = 700;
    private const double MinimumHeight = 500;

    internal SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();

        Title = "ScrubShot Settings";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "scrubshot.ico"));

        Root.Loaded += OnRootLoaded;
        Activated += OnActivated;
        ViewModel.Refreshed += OnViewModelRefreshed;
        Closed += (_, _) => ViewModel.Refreshed -= OnViewModelRefreshed;

        Nav.SelectedItem = Nav.MenuItems[0];
        ApplyTheme();
    }

    internal SettingsViewModel ViewModel { get; }

    /// <summary>Sizes the window for the monitor it opened on, never larger than that monitor's work area, and centres it there.</summary>
    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        Root.Loaded -= OnRootLoaded;
        var scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var width = Math.Min((int)(DefaultWidth * scale), work.Width);
        var height = Math.Min((int)(DefaultHeight * scale), work.Height);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Math.Min((int)(MinimumWidth * scale), work.Width);
            presenter.PreferredMinimumHeight = Math.Min((int)(MinimumHeight * scale), work.Height);
        }

        AppWindow.MoveAndResize(new RectInt32(work.X + ((work.Width - width) / 2), work.Y + ((work.Height - height) / 2), width, height));
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            ViewModel.OnWindowActivated();
        }
    }

    /// <summary>
    /// The one place bindings are refreshed. It is queued rather than run inline because the view model raises this
    /// from inside a control's own change (a toggle pushing its value), and writing to that control there is unsafe.
    /// </summary>
    private void OnViewModelRefreshed()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            Bindings.Update();
            ApplyTheme();
        });
    }

    private void ApplyTheme()
    {
        var theme = ViewModel.Theme;
        Root.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        AppWindow.TitleBar.PreferredTheme = theme switch
        {
            AppTheme.Light => TitleBarTheme.Light,
            AppTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "General";
        GeneralPanel.Visibility = page == "General" ? Visibility.Visible : Visibility.Collapsed;
        HotkeysPanel.Visibility = page == "Hotkeys" ? Visibility.Visible : Visibility.Collapsed;
        CapturePanel.Visibility = page == "Capture" ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = page;
    }

    /// <summary>
    /// Enter commits a text box in place. Focus stays in the box: moving it to the next control would put it on a
    /// button (Browse, Reset to defaults), where a second or held Enter would press that button.
    /// </summary>
    private void OnTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        if (!e.KeyStatus.WasKeyDown && sender is TextBox { Tag: string property } box)
        {
            ViewModel.Commit(property, box.Text);
        }
    }

    private async void OnBrowseFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker(AppWindow.Id);
            var result = await picker.PickSingleFolderAsync();
            if (result is not null)
            {
                ViewModel.SaveFolder = result.Path;
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // The picker is a convenience; the folder can still be typed.
        }
    }
}
