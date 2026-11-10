// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace ScrubShot.App.Settings;

/// <summary>
/// A row in the Settings window. The element placed inside the card is the setting's control; when
/// <see cref="IsLocked"/> is true the card shows a lock and disables that control, so a setting fixed by policy is
/// visibly read-only.
/// </summary>
[ContentProperty(Name = nameof(ActionContent))]
public sealed partial class SettingCard : UserControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ErrorProperty =
        DependencyProperty.Register(nameof(Error), typeof(string), typeof(SettingCard), new PropertyMetadata(string.Empty, OnErrorChanged));

    public static readonly DependencyProperty IsLockedProperty =
        DependencyProperty.Register(nameof(IsLocked), typeof(bool), typeof(SettingCard), new PropertyMetadata(false));

    public static readonly DependencyProperty ActionContentProperty =
        DependencyProperty.Register(nameof(ActionContent), typeof(object), typeof(SettingCard), new PropertyMetadata(null));

    public SettingCard()
    {
        InitializeComponent();
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Shown in the critical color under the description when the value on screen could not be saved.</summary>
    public string Error
    {
        get => (string)GetValue(ErrorProperty);
        set => SetValue(ErrorProperty, value);
    }

    public bool IsLocked
    {
        get => (bool)GetValue(IsLockedProperty);
        set => SetValue(IsLockedProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    public static Visibility VisibleWhenNotEmpty(string? text) =>
        string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    public static bool Not(bool value) => !value;

    /// <summary>
    /// A screen reader is only told about a new error when a LiveRegionChanged event is raised; marking the text as
    /// a live region is not enough. The raise is queued so it runs after the binding has put the new text on screen.
    /// </summary>
    private static void OnErrorChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not SettingCard card || string.IsNullOrEmpty(args.NewValue as string) || Equals(args.NewValue, args.OldValue))
        {
            return;
        }

        card.DispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (card.IsLoaded && card.ErrorText.Visibility == Visibility.Visible)
            {
                FrameworkElementAutomationPeer.FromElement(card.ErrorText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
        });
    }
}
