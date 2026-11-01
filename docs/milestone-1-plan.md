# Milestone 1 plan: foundation and capture

**Status:** approved by the owner on 2026-11-01. In progress on branch `milestone-1-capture`; step 1 (scaffold) complete. Progress is tracked in `docs/status.md`.

## Goal

From the spec: solution scaffold, tray app, hotkeys with conflict detection, frozen overlay, region/window/full/monitor/repeat capture, mixed-DPI correctness, copy and save.

**Done when:** captures are pixel-accurate on a mixed-DPI two-monitor setup and Print Screen goes straight to region select.

## Build machine today

Checked on 2026-11-01 on the development PC (Windows 11 Pro for Workstations, build 26300).

| Item | State | Action |
| --- | --- | --- |
| .NET SDK | **Missing.** Only the 6.0 and 8.0 runtimes are present. | Owner runs `winget install Microsoft.DotNet.SDK.10` in an elevated terminal (the installer needs UAC). |
| WinUI `dotnet new` templates | Not installed | `dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates` after the SDK. |
| Visual Studio / Build Tools / Windows SDK | Not installed | Not required. Microsoft's documented Visual Studio-free path is .NET SDK 10 + templates + `dotnet build`; the Windows SDK build tools arrive as a NuGet package. Verify at first build. |
| Windows App Runtime | 1.2 through 2.6.4 present | Nothing to do (irrelevant once self-contained). |
| GitHub CLI | 2.103.3 installed and authenticated as `emichael14` (scopes: gist, read:org, repo, workflow) | Installed this morning; shells opened before then do not have it on PATH. Open a new terminal. |
| Git identity | Eric Michael, configured globally | Leave as is. |
| winapp CLI (`Microsoft.winappcli`) | Not installed; the package id from Microsoft docs could not be confirmed via `winget search` from the session | Only needed for MSIX packaging in Milestone 6. |
| Monitors | One 1920x1080 display at 100% | The mixed-DPI acceptance test needs the owner's two-monitor setup or a second display. See Verification. |

## Solution structure

```
scrubshot/
├── ScrubShot.sln
├── global.json                  pins .NET SDK 10.0.4xx, rollForward latestFeature
├── Directory.Build.props        TFM, nullable, warnings as errors, version, SPDX/copyright
├── Directory.Packages.props     central package version management
├── .editorconfig
├── src/
│   ├── ScrubShot.Core/          net10.0-windows. Settings model + JSON store, policy resolver,
│   │                            file naming patterns, capture geometry, logging abstractions.
│   ├── ScrubShot.Capture/       net10.0-windows. Monitor enumeration, IScreenCapturer + GDI
│   │                            implementation, FrozenScreen, selection overlay (Win32 + GDI),
│   │                            window picker, cursor capture. CsWin32. No WinUI.
│   └── ScrubShot.App/           net10.0-windows10.0.26100.0, WinUI 3, unpackaged, self-contained.
│                                Tray (H.NotifyIcon), hotkey service, capture coordinator,
│                                output (clipboard, file), minimal Settings window, notifications.
├── tests/
│   └── ScrubShot.Tests/         xUnit. Core + Capture geometry tests. Fixture folders for later.
├── tools/
│   └── CaptureCheck/            console: captures all monitors to PNG, prints the monitor table.
│                                Used to verify pixel accuracy on real hardware.
├── deploy/                      empty until Milestone 6 (README placeholder)
├── docs/
└── .github/
    ├── workflows/ci.yml         windows-latest: restore, build, test
    └── ISSUE_TEMPLATE/
```

Detection, Clipboard and AddIns projects are added in their own milestones so the solution never carries empty shells.

## Work breakdown

Each step ends with a passing build, passing tests and a focused commit on `milestone-1-capture` (or a feature branch off it).

1. **Scaffold.** Solution, props files, `global.json`, `.editorconfig`, the three `src` projects, the test project, CI workflow, SPDX headers. The app starts, shows a placeholder tray icon with Exit, and quits cleanly. `dotnet build` and `dotnet test` are green locally and in CI.

2. **Core: settings and policy.** Settings records for General, Output, Capture, Hotkeys, Advanced. Versioned JSON store with atomic writes. Policy resolver with precedence machine policy > user policy > user settings > defaults, returning value and source. File name pattern engine with tokens (`${YYYY}`, `${MM}`, `${DD}`, `${hh}`, `${mm}`, `${ss}`, `${NUM}`; `${title}` reserved for later) and collision handling. Unit tests for precedence, locks, pattern expansion and invalid-character sanitizing.

3. **Capture engine.** `MonitorLayout` from `EnumDisplayMonitors` + `GetDpiForMonitor` + virtual screen bounds. `IScreenCapturer` with `GdiScreenCapturer` producing a `FrozenScreen` (composite 32-bit bitmap in physical pixels, per-monitor rectangles and DPI, timestamp). Optional cursor compositing (`GetCursorInfo`, `DrawIconEx`) and capture delay. Geometry helpers in Core (virtual to bitmap to monitor-local coordinates, clamping, rectangle normalization) with unit tests over synthetic layouts: 150% laptop left of a 100% monitor, negative origins, vertically stacked monitors, primary not at 0,0.

4. **Selection overlay.** Per-monitor topmost Win32 windows showing the dimmed frozen image. Region drag with live size readout, crosshair, magnifier (toggle with M), arrow-key nudge of the selection edge, Enter/Space to confirm, Esc to cancel. Window-pick mode highlights the window under the cursor using `DWMWA_EXTENDED_FRAME_BOUNDS`, skipping cloaked and tool windows; click confirms. Full-screen and single-monitor modes bypass the overlay. Last region is remembered for repeat capture. Cross-monitor drags work through the shared selection model.

5. **Hotkeys.** `HotkeyService` on a hidden message window using `RegisterHotKey`. Defaults from the spec, remappable through settings and policy. Conflict detection on registration; the Print Screen case names Snipping Tool and offers the one-click fix (D-013). Re-registration when settings change, without restart.

6. **Tray app and coordination.** Tray menu: Capture region, Capture window, Capture full screen, Capture monitor (submenu per monitor), Repeat last region, Open last capture, Settings, Exit. Single instance via named mutex; a second launch activates the first. Launch at startup through the `HKCU\...\Run` key, policy-aware. `CaptureCoordinator` ties hotkey → capture → overlay → output and guarantees the frozen bitmap is disposed when the flow ends or is cancelled.

7. **Output.** Clipboard write as PNG and CF_DIB with history and cloud-sync exclusion (D-012). File save as PNG or JPG into the configured folder using the naming pattern. Default action setting: Copy, Save, or Copy and Save. A local notification confirms the result with an action to open the folder. Open last capture reopens the most recent saved file, or re-copies the last in-memory result if nothing was saved.

8. **Minimal Settings window.** WinUI 3 window with Mica. Tabs: General (startup, save folder, naming, format, default action, theme), Hotkeys (remap, conflict status, fix button), Capture (cursor, delay, magnifier, coordinates). Settings locked by policy render read-only with a lock icon. The full tab set arrives in Milestone 5.

9. **Verification, docs, PR.** `CaptureCheck` tool; manual test checklist in `docs/testing/milestone-1-checklist.md`; README updated with real build and run steps; `docs/decisions.md` updated with anything decided along the way; pull request into `main` summarizing what was built, how to test it and the acceptance criteria met.

10. **Contributor license agreement.** Added to this milestone on 2026-11-01 (the spec had it in Milestone 6) so code contributions can open as soon as Milestone 1 merges. Add the CLA text at `docs/CLA.md` with the placeholder entity from D-015, and the CLA Assistant check as a GitHub Actions workflow (`contributor-assistant/github-action`), which stores signatures in the repository rather than a third-party service. Update the README Contributing section when it is live.

## Key design points

Details and rationale are in `docs/decisions.md` entries D-003 through D-014. In one line each:

- PerMonitorV2 process; every capture coordinate is a physical pixel.
- GDI BitBlt per monitor now, Windows.Graphics.Capture later behind `IScreenCapturer`.
- Overlay is Win32 and GDI, one window per monitor, no XAML in the hot path.
- The frozen bitmap is the only copy of the screen, lives in memory only, and is disposed deterministically.
- Settings resolve through policy from day one; the UI shows what is locked and by whom.
- Logging cannot accept captured content.
- Third-party packages: CsWin32, H.NotifyIcon.WinUI, CommunityToolkit.Mvvm, xUnit. All MIT, none make network calls.

## Verification

**Unit tests (CI).** Settings precedence and locks, naming patterns, capture geometry on synthetic mixed-DPI layouts, overlay selection model (drag across monitors, clamping, keyboard nudge).

**Headless check (real hardware).** `tools/CaptureCheck` captures all monitors, writes one PNG per monitor plus the composite into a chosen folder, and prints the monitor table (bounds, DPI, scale). Comparing the PNG dimensions and a few known pixels against the live screen is the pixel-accuracy test.

**Manual checklist.** Print Screen opens region select within perceptible-instant time; region drag across the DPI boundary selects exactly what is shown; window capture of a window straddling two monitors has no blur or offset; full screen and per-monitor captures match native resolution; repeat last region works after a monitor layout change (degrades gracefully); conflict dialog appears when Snipping Tool owns Print Screen and the fix works; captures excluded from clipboard history (Win+V shows nothing new); no files appear anywhere unless Save is the chosen action.

**Mixed-DPI limitation.** The development PC currently has a single 100% display. The synthetic-layout tests and a 150% single-monitor run cover the arithmetic; the acceptance criterion itself needs the owner's two-monitor setup or a second display attached before the PR is marked ready.

## Risks and open items

1. **Per-monitor overlay windows versus one spanning window (D-006).** Decided per-monitor; if cross-monitor drag feels wrong on real hardware, the spanning window is the fallback and the selection model is shared either way.
2. **Visual Studio-free WinUI build.** Microsoft documents it as supported with .NET SDK 10. If `dotnet build` needs the Windows SDK installed after all, the fallback is `winget install Microsoft.WindowsSDK.10.0.26100` (no Visual Studio).
3. **H.NotifyIcon with Windows App SDK 2.x.** Verify compatibility at step 1; fall back to our own Shell_NotifyIcon interop.
4. **Unpackaged AppNotification.** Needs `AppNotificationManager.Register()` at startup; verify it works self-contained. Fallback for Milestone 1 is a tray balloon.
5. **Snipping Tool registry switch.** Verify on this machine that toggling `PrintScreenKeyForSnippingEnabled` actually releases Print Screen on build 26300.

## Assumptions made (say so if any is wrong)

- Adding `src/ScrubShot.Capture` and using `src/` and `tests/` folders is acceptable.
- The three MIT dependencies above need no separate approval (none phones home, none needs a key, all GPL-compatible).
- Minimum OS Windows 10 2004 rather than Windows 11 only.
- A minimal Settings window belongs in Milestone 1.
- Clipboard-history exclusion moves from Milestone 4 to Milestone 1.

## Out of scope for Milestone 1

Post-capture menu, editor, OCR and detection, clipboard monitor, full Settings tabs, ADMX templates, add-ons, installer, MSIX, code signing, update check, final icon and bubble animation.
