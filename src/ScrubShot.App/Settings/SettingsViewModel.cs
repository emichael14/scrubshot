// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScrubShot.Core.Hotkeys;
using ScrubShot.Core.Output;
using ScrubShot.Core.Settings;
using ScrubShot.Output;

namespace ScrubShot.App.Settings;

/// <summary>What the Settings window needs from the running app. The app implements it; nothing here touches WinUI.</summary>
internal interface ISettingsHost
{
    /// <summary>Settings in effect, with the source of each (default, user, policy).</summary>
    EffectiveSettings Effective { get; }

    /// <summary>The outcome of the last hotkey registration, one entry per action.</summary>
    IReadOnlyList<HotkeyBinding> HotkeyBindings { get; }

    string? PicturesFolder { get; }

    /// <summary>Validates, saves and applies a change. Edits to settings locked by policy are dropped by the service.</summary>
    SettingsChangeResult TryChange(ScrubShotSettings proposed);

    /// <summary>Reads policy again, so a lock that arrived while the window was in the background shows when it comes forward.</summary>
    void RefreshPolicy();

    /// <summary>Turns off the Windows switch that sends Print Screen to the Snipping Tool, then registers the hotkeys again.</summary>
    void FixSnippingToolConflict();
}

/// <summary>
/// The Settings window's state. Every change is applied at once: a setter proposes the change through a
/// <see cref="SettingsEditSession"/>, which holds the rules (refused values stay on screen with their reason and
/// are tried again when they may have become valid; locked settings cannot change), and the window then re-reads
/// every binding. There are no per-property notifications: <see cref="Refreshed"/> is the one signal, and the
/// window answers it with a deferred update of all bindings, so a control is never written to from inside its own
/// change (D-046). It derives from <see cref="ObservableObject"/> so the window's bindings have a well-formed,
/// notifying source; it never raises a per-property notification.
/// </summary>
internal sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsHost _host;
    private readonly SettingsEditSession _session;

    public SettingsViewModel(ISettingsHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _session = new SettingsEditSession(() => _host.Effective, _host.TryChange);
        FixSnippingToolCommand = new RelayCommand(FixSnippingTool);
        ResetHotkeysCommand = new RelayCommand(ResetHotkeys);
    }

    /// <summary>Raised after anything changed. The window re-reads every binding, including the lock and message functions.</summary>
    public event Action? Refreshed;

    public RelayCommand FixSnippingToolCommand { get; }

    public RelayCommand ResetHotkeysCommand { get; }

    public AppTheme Theme => Values.General.Theme;

    /// <summary>The last change could not be written to the settings file.</summary>
    public bool SaveFailed => _session.SaveFailed;

    /// <summary>The last hotkey reset had to leave a hotkey as it was.</summary>
    public bool ResetWasPartial => _session.ResetWasPartial;

    // General

    public bool LaunchAtStartup
    {
        get => Values.General.LaunchAtStartup;
        set => Apply("General.LaunchAtStartup", value != Values.General.LaunchAtStartup, v => v with { General = v.General with { LaunchAtStartup = value } });
    }

    public int ThemeIndex
    {
        get => (int)Values.General.Theme;
        set => ApplyEnum<AppTheme>("General.Theme", value, (int)Values.General.Theme, (v, theme) => v with { General = v.General with { Theme = theme } });
    }

    public int DefaultActionIndex
    {
        get => (int)Values.Output.DefaultAction;
        set => ApplyEnum<DefaultCaptureAction>("Output.DefaultAction", value, (int)Values.Output.DefaultAction, (v, action) => v with { Output = v.Output with { DefaultAction = action } });
    }

    public bool ExcludeFromClipboardHistory
    {
        get => Values.Output.ExcludeFromClipboardHistory;
        set => Apply("Output.ExcludeFromClipboardHistory", value != Values.Output.ExcludeFromClipboardHistory, v => v with { Output = v.Output with { ExcludeFromClipboardHistory = value } });
    }

    public string SaveFolder
    {
        get => Text("Output.SaveFolder", Values.Output.SaveFolder);
        set => ApplyText("Output.SaveFolder", value, Values.Output.SaveFolder, (v, text) => v with { Output = v.Output with { SaveFolder = text } });
    }

    /// <summary>Where saves go with the folder now in effect, or why they cannot.</summary>
    public string SaveFolderNote =>
        Core.Output.SaveFolder.TryResolve(Values.Output.SaveFolder, _host.PicturesFolder, out var folder, out var problem)
            ? $"Captures are saved to {folder}"
            : Core.Output.SaveFolder.Describe(problem);

    public string FileNamePattern
    {
        get => Text("Output.FileNamePattern", Values.Output.FileNamePattern);
        set => ApplyText("Output.FileNamePattern", value, Values.Output.FileNamePattern, (v, text) => v with { Output = v.Output with { FileNamePattern = text } });
    }

    public string FileNameNote =>
        $"Example: {CaptureSaver.FileNameFor(Values.Output, DateTimeOffset.Now)}. Tokens: ${{YYYY}} ${{MM}} ${{DD}} ${{hh}} ${{mm}} ${{ss}} ${{NUM}}";

    public int FormatIndex
    {
        get => (int)Values.Output.Format;
        set => ApplyEnum<ImageFormat>("Output.Format", value, (int)Values.Output.Format, (v, format) => v with { Output = v.Output with { Format = format } });
    }

    public bool IsJpg => Values.Output.Format == ImageFormat.Jpg;

    public double JpgQuality
    {
        get => Values.Output.JpgQuality;
        set => ApplyNumber("Output.JpgQuality", value, Values.Output.JpgQuality, (v, quality) => v with { Output = v.Output with { JpgQuality = quality } });
    }

    // Hotkeys

    public string RegionHotkey
    {
        get => Text("Hotkeys.Region", Values.Hotkeys.Region);
        set => ApplyText("Hotkeys.Region", value, Values.Hotkeys.Region, (v, text) => v with { Hotkeys = v.Hotkeys with { Region = text } });
    }

    public string WindowHotkey
    {
        get => Text("Hotkeys.Window", Values.Hotkeys.Window);
        set => ApplyText("Hotkeys.Window", value, Values.Hotkeys.Window, (v, text) => v with { Hotkeys = v.Hotkeys with { Window = text } });
    }

    public string FullScreenHotkey
    {
        get => Text("Hotkeys.FullScreen", Values.Hotkeys.FullScreen);
        set => ApplyText("Hotkeys.FullScreen", value, Values.Hotkeys.FullScreen, (v, text) => v with { Hotkeys = v.Hotkeys with { FullScreen = text } });
    }

    public string RepeatLastRegionHotkey
    {
        get => Text("Hotkeys.RepeatLastRegion", Values.Hotkeys.RepeatLastRegion);
        set => ApplyText("Hotkeys.RepeatLastRegion", value, Values.Hotkeys.RepeatLastRegion, (v, text) => v with { Hotkeys = v.Hotkeys with { RepeatLastRegion = text } });
    }

    /// <summary>True while Windows routes Print Screen to the Snipping Tool and a ScrubShot hotkey wants that key.</summary>
    public bool ShowSnippingToolFix => _host.HotkeyBindings.Any(b => b.Conflict == HotkeyConflict.OwnedBySnippingTool);

    // Capture

    public bool CaptureCursor
    {
        get => Values.Capture.CaptureCursor;
        set => Apply("Capture.CaptureCursor", value != Values.Capture.CaptureCursor, v => v with { Capture = v.Capture with { CaptureCursor = value } });
    }

    public double DelayMilliseconds
    {
        get => Values.Capture.DelayMilliseconds;
        set => ApplyNumber("Capture.DelayMilliseconds", value, Values.Capture.DelayMilliseconds, (v, delay) => v with { Capture = v.Capture with { DelayMilliseconds = delay } });
    }

    public bool ShowMagnifier
    {
        get => Values.Capture.ShowMagnifier;
        set => Apply("Capture.ShowMagnifier", value != Values.Capture.ShowMagnifier, v => v with { Capture = v.Capture with { ShowMagnifier = value } });
    }

    public bool ShowCoordinates
    {
        get => Values.Capture.ShowCoordinates;
        set => Apply("Capture.ShowCoordinates", value != Values.Capture.ShowCoordinates, v => v with { Capture = v.Capture with { ShowCoordinates = value } });
    }

    private ScrubShotSettings Values => _host.Effective.Values;

    /// <summary>True when policy sets this value; the card shows a lock and its control is disabled.</summary>
    public bool IsLocked(string path) => _host.Effective.IsLocked(path);

    /// <summary>The message for a value the user typed that could not be saved, or empty.</summary>
    public string ErrorFor(string path) => _session.ProblemFor(path) is { } problem ? Describe(problem) : string.Empty;

    /// <summary>"Registered" when the action's hotkey is live and nothing else is pending for it, otherwise empty (the problem is shown instead).</summary>
    public string HotkeyNote(string action) =>
        !_session.HasPending($"Hotkeys.{action}") && BindingFor(action) is { IsRegistered: true } ? "Registered" : string.Empty;

    /// <summary>Why the action's hotkey is not live, or empty when it is.</summary>
    public string HotkeyProblem(string action)
    {
        var typed = ErrorFor($"Hotkeys.{action}");
        if (typed.Length > 0)
        {
            return typed;
        }

        return BindingFor(action) switch
        {
            null => "Not registered: the hotkey service is not running",
            { IsRegistered: true } => string.Empty,
            { Conflict: HotkeyConflict.OwnedBySnippingTool } => "Windows sends this key to the Snipping Tool",
            { Conflict: HotkeyConflict.OwnedByAnotherApp } => "In use by another app",
            { Conflict: HotkeyConflict.InvalidGesture } => Describe(SettingProblem.HotkeyInvalid),
            { Conflict: HotkeyConflict.ReservedKey } => Describe(SettingProblem.HotkeyReservedKey),
            { Conflict: HotkeyConflict.NeedsModifier } => Describe(SettingProblem.HotkeyNeedsModifier),
            { Conflict: HotkeyConflict.DuplicateInSettings } => Describe(SettingProblem.HotkeyDuplicate),
            var other => string.Create(CultureInfo.InvariantCulture, $"Could not be registered (error {other.ErrorCode})"),
        };
    }

    /// <summary>Commits a text box in place when Enter is pressed. <paramref name="property"/> is the name the box carries in its Tag.</summary>
    public void Commit(string property, string text)
    {
        switch (property)
        {
            case nameof(SaveFolder): SaveFolder = text; break;
            case nameof(FileNamePattern): FileNamePattern = text; break;
            case nameof(RegionHotkey): RegionHotkey = text; break;
            case nameof(WindowHotkey): WindowHotkey = text; break;
            case nameof(FullScreenHotkey): FullScreenHotkey = text; break;
            case nameof(RepeatLastRegionHotkey): RepeatLastRegionHotkey = text; break;
        }
    }

    /// <summary>The window came forward: read policy again so a lock that arrived meanwhile is shown.</summary>
    public void OnWindowActivated()
    {
        _host.RefreshPolicy();
        Refresh();
    }

    /// <summary>The app reports that settings changed from outside this window (a policy refresh, a reload).</summary>
    public void OnSettingsChangedElsewhere()
    {
        _session.OnSettingsChangedElsewhere();
        Refresh();
    }

    internal static string Describe(SettingProblem problem) => problem switch
    {
        SettingProblem.HotkeyInvalid => "Not a valid shortcut. Examples: PrintScreen, Ctrl+Shift+S, Alt+F9",
        SettingProblem.HotkeyReservedKey => "F12 is reserved by Windows",
        SettingProblem.HotkeyNeedsModifier => "Add Ctrl, Alt, Shift or Win to this key",
        SettingProblem.HotkeyDuplicate => "Already used by another action",
        SettingProblem.SaveFolderNotFullyQualified => Core.Output.SaveFolder.Describe(SaveFolderProblem.NotFullyQualified),
        SettingProblem.SaveFolderPicturesUnavailable => Core.Output.SaveFolder.Describe(SaveFolderProblem.PicturesUnavailable),
        SettingProblem.SaveFolderUnresolvedVariable => Core.Output.SaveFolder.Describe(SaveFolderProblem.UnresolvedVariable),
        SettingProblem.SaveFolderInvalid => Core.Output.SaveFolder.Describe(SaveFolderProblem.Invalid),
        SettingProblem.JpgQualityOutOfRange => "Choose a quality from 1 to 100",
        SettingProblem.DelayOutOfRange => "Choose a delay from 0 to 10000 milliseconds",
        _ => "This value is not valid",
    };

    private HotkeyBinding? BindingFor(string action) =>
        Enum.TryParse<HotkeyAction>(action, out var parsed) ? _host.HotkeyBindings.FirstOrDefault(b => b.Action == parsed) : null;

    private string Text(string path, string saved) => _session.PendingText(path) ?? saved;

    private void ApplyText(string path, string? typed, string saved, Func<ScrubShotSettings, string, ScrubShotSettings> change)
    {
        var text = (typed ?? string.Empty).Trim();
        if (!_session.HasPending(path) && text == saved)
        {
            // Nothing to save. If only whitespace differed, put the saved text back in the box.
            if (!string.Equals(typed, text, StringComparison.Ordinal))
            {
                Refresh();
            }

            return;
        }

        if (text == saved)
        {
            _session.Revert(path); // back to the saved value: drop the earlier message
        }
        else
        {
            // Also reached when the same refused text is committed again: what was wrong with it may be gone.
            _session.Change(path, v => change(v, text), text);
        }

        Refresh();
    }

    private void ApplyEnum<TEnum>(string path, int index, int current, Func<ScrubShotSettings, TEnum, ScrubShotSettings> change)
        where TEnum : struct, Enum
    {
        // A ComboBox reports -1 while it has no selection (during load); that is not a choice.
        if (index == current || !Enum.IsDefined(typeof(TEnum), index))
        {
            return;
        }

        var value = (TEnum)Enum.ToObject(typeof(TEnum), index);
        Apply(path, true, v => change(v, value));
    }

    private void ApplyNumber(string path, double value, int current, Func<ScrubShotSettings, int, ScrubShotSettings> change)
    {
        // A cleared NumberBox reports NaN; put the saved number back.
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            Refresh();
            return;
        }

        var rounded = (int)Math.Clamp(Math.Round(value), int.MinValue, int.MaxValue);
        if (rounded == current)
        {
            // Typed 90.4 over a saved 90: nothing to save, but show the saved number again.
            if (value != rounded)
            {
                Refresh();
            }

            return;
        }

        Apply(path, true, v => change(v, rounded));
    }

    private void Apply(string path, bool differs, Func<ScrubShotSettings, ScrubShotSettings> change)
    {
        if (!differs)
        {
            return;
        }

        _session.Change(path, change);
        Refresh();
    }

    private void FixSnippingTool()
    {
        _host.FixSnippingToolConflict();
        Refresh();
    }

    private void ResetHotkeys()
    {
        _session.ResetHotkeys();
        Refresh();
    }

    private void Refresh() => Refreshed?.Invoke();
}
