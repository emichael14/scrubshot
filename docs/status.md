# Project status

Updated at the end of every session. Newest entry first. Keep each entry short: where things stand, what is blocked, what comes next.

## 2026-11-01 (session 2)

**Milestone:** 1 (foundation and capture). **Steps 1 (scaffold), 2 (Core settings and policy), 3 (capture engine), 4 (selection overlay), 5 (hotkeys) and 6 (tray menu, coordinator, single instance, startup) done** on branch `milestone-1-capture`, rebased onto `main` after PR #1 merged.

**Step 6 summary (same day, later).** `ScrubShot.Core.Coordination` (`CaptureMode`, `CaptureRequest`, `CaptureOutcome`), `ScrubShot.Core.Lifecycle` (`SingleInstance`, `StartupRegistration` with a registry and an in-memory store), `ScrubShot.Capture.Coordination.CaptureCoordinator` with `IRegionSelector` (overlay) and `ICaptureSink` (`DiscardingCaptureSink` until step 7): delay, freeze, select, crop, hand off; frozen screen disposed on every path; one capture at a time; repeat last region with reconciliation and fallback (D-040). The app takes the instance mutex first and exits if another instance holds it (D-041), syncs the Run key with the effective setting (D-042), builds the tray menu in code (Capture region, window, full screen, monitor submenu refreshed on right-click, Repeat last region, Open last capture and Settings disabled until steps 7 and 8, Exit), and routes hotkey presses and menu clicks to the coordinator on the thread pool. `CaptureCheck --flow region|window|full|monitor|repeat <folder> [index] [--busy]` runs the coordinator end to end. Verified here: full-screen and monitor flows wrote a 1920x1080 `flow.png`, a bad monitor index returned `NoMonitor` without capturing, a second request during a flow returned `Busy`; the app ran, a second launch exited with code 0 while the first stayed alive, and the Run value appeared with the Debug exe path (removed again afterwards; it was absent before). 218 tests. Light review per the review-tier rule; outcome in Done.

**Step 5 summary (same day, later).** `ScrubShot.Core.Hotkeys`: `HotkeyGesture` (parse and format), `HotkeyBindings` (actions, duplicates), `SnippingToolPrintScreen` (Windows 11 switch with the correct absent-means-on default, one-click fix). `ScrubShot.Capture.Hotkeys.HotkeyService`: dedicated thread, message-only window, `RegisterHotKey` with `MOD_NOREPEAT`, per-action conflict classification, re-apply without restart (D-039). The app registers the configured hotkeys at launch and logs counts and conflicts as enums only; the coordinator that acts on a press is step 6. `CaptureCheck --hotkeys [seconds] [four gestures]` prints bindings and presses. Verified here: with the defaults the service reported Print Screen owned by the Snipping Tool (Windows 11 default) and the three modified combinations owned by another app, which turned out to be Greenshot running on this machine; with four free gestures every injected press arrived at the right action. 187 tests. Light review per the review-tier rule; outcome in Done.

**Step 4 summary (same day, later).** `ScrubShot.Core.Selection`: `RegionSelection` state machine (inclusive rectangles, clamping, nudge, keyboard anchor), `WindowPick`, `LastRegion` (D-038). `ScrubShot.Capture.Overlay`: `OverlaySession` (one topmost GDI window per monitor, dimmed base, bright selection, crosshair, size and coordinate label, 4x magnifier, keyboard-complete input, dedicated STA thread), `WindowFinder` (z-ordered pickable windows via extended frame bounds), `OverlayOptions` and `OverlayResult`. `CaptureCheck --overlay region|window` drives it; a scripted drag, Escape, keyboard-only selection and window pick all returned the expected regions on this machine, and mid-drag screenshots confirmed the rendering. Two input bugs found by the script and fixed: positions are now taken from the mouse message rather than polled, and Shift is read from both thread and physical key state. A CsWin32 marshalling defect in the generated `CreateFont` wrapper is avoided by using `CreateFontIndirect`. 140 tests. Light review (one lens, one refuter) per the new review-tier rule; outcome in Done.

**Step 3 summary (same day, later).** `ScrubShot.Core.Geometry`: `PixelPoint`, `PixelSize`, `PixelRect`, `MonitorInfo`, `MonitorLayout` with virtual-screen, bitmap and monitor-local conversions (D-035). `ScrubShot.Capture`: `MonitorEnumerator` (EnumDisplayMonitors, GetMonitorInfo, GetDpiForMonitor), `IScreenCapturer`, `GdiScreenCapturer` (one top-down DIB section, BitBlt per monitor with CAPTUREBLT, optional cursor), `FrozenScreen` (zeroed on dispose, crop to `CapturedImage`) (D-036). `tools/CaptureCheck` landed early (D-037): on this machine it captured 1920x1080 with the cursor in 51 ms and the PNG matched the live screen. 110 tests, including synthetic mixed-DPI layouts (150 percent laptop beside 100 percent monitor, negative origin, stacked). An adversarial review workflow ran on the step 3 code before commit; its outcome is recorded below in Done.

**Step 2 summary (same day, later).** `ScrubShot.Core.Settings`: immutable settings records; `SettingsSchema` (leaf paths, types, defaults derived from the record shape); `SettingsDocument` (JSON normalization: case-insensitive names, per-leaf reset of invalid values); `JsonSettingsStore` (atomic save, timestamped set-aside, unreadable-file protection, newer-schema backup); `IPolicySource` with registry and in-memory implementations; `SettingsResolver` (machine policy > user policy > user > defaults with per-setting source and lock). `ScrubShot.Core.Output`: `FileNamePattern` (tokens, sanitizing, reserved names) and `UniqueFileName` (collision suffix). The app loads and resolves settings at launch. 93 tests. An adversarial review workflow (3 lenses, every finding refuted by two independent checkers) confirmed 9 distinct issues, all fixed before commit; decisions D-031 to D-034.

**Done**

- Toolchain: .NET SDK 10.0.403 present. WinUI templates installed after adding the missing `nuget.org` source to the user NuGet config (the fresh SDK had none; details in `CLAUDE.local.md`).
- Solution `ScrubShot.sln` with `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig` (license header enforced at build), `src/ScrubShot.Core`, `src/ScrubShot.Capture`, `src/ScrubShot.App`, `tests/ScrubShot.Tests`, `.github/workflows/ci.yml`, `deploy/README.md`.
- Core: `Logging.Log`, the single content-free logging surface (D-014, D-026) with a reflection test guarding its parameter types.
- Capture: CsWin32 wired through `NativeMethods.txt`; `ProcessDpi.IsPerMonitorV2()` startup check (D-027).
- App: unpackaged, self-contained WinUI 3, PerMonitorV2 manifest, windowless, placeholder generated tray icon with Exit via H.NotifyIcon.WinUI 2.4.1 on Windows App SDK 2.6.4.
- Verified locally: `dotnet build` Debug and Release with 0 warnings; `dotnet test` 3/3 passing in MTP mode; the app launched, stayed alive, and Windows registered its tray icon (`HKCU\Control Panel\NotifyIconSettings` entry for `ScrubShot.exe`). Exit through the tray menu was not exercised by automation; owner to click it once.
- Commits pushed to `origin/milestone-1-capture`. First CI run green (restore, Release build, MTP test, TRX artifact) in 1m33s on windows-latest.
- Decisions D-021 to D-027 recorded.
- Later the same day: `CLAUDE.md` gained the autonomy rule for failed checks; `tools/IconBuild` added; original app icon designed (five directions, judged, refined, owner chose the diagonal trio with a three-bubble tray variant) and wired into the app, README and `assets/icon` (D-028). Owner confirmed the tray icon shows and Exit works.
- Step 6 review (light tier, one lens, one refuter): 1 finding confirmed and fixed before commit: the activation event is now created before the instance mutex is taken and the watcher starts only after the app has subscribed, so a second launch in the first instance's startup window can no longer lose its wake-up signal (test added for a signal that arrives before start). 1 finding refuted: the refuter verified against the H.NotifyIcon 2.4.1 source that `RightClickCommand` runs before the native menu is built and the menu is rebuilt from the flyout on every show, so the monitor submenu is always current; the verified ordering is recorded in a code comment.
- Step 5 review (light tier, one lens, one refuter): 6 findings confirmed, all fixed before commit: `Invoke` runs inline on the hotkey thread and times out instead of blocking forever, and posting after the window is gone fails fast; handler exceptions are caught inside the window procedure and reported through `HandlerFailed` (log event 1204) rather than unwinding into native frames; duplicate gestures within the settings are classified as `DuplicateInSettings` instead of "owned by another app"; a bare letter, digit, Enter or Space is refused as `NeedsModifier` so it is never taken from every application (Print Screen, Pause, Scroll Lock and F-keys may stand alone); the startup failure log carries the Win32 error code, not the wrapper HRESULT; window-class registration is locked and treats "class already exists" as success. Verified live: `--hotkeys 5 Ctrl+Shift+F9 ctrl+shift+f9 A Ctrl+Shift+F8` reported registered, DuplicateInSettings, NeedsModifier, registered, and both injected presses arrived.
- Step 4 review (light tier, one lens, one refuter): 7 findings confirmed, all fixed before commit: click inside a selected region confirms it; window mode keeps the pointer and routes arrows to re-hit-testing instead of resizing; label font, padding and magnifier zoom scale per monitor DPI; the outcome is set once and late input is ignored; window construction is leak-safe on failure; all windows are shown only once prepared and the dim pass uses a lookup table; click-through windows are never pick candidates.
- Step 3 review: the adversarial workflow (Win32, memory and privacy, geometry lenses; two refuters per finding) confirmed 15 low-severity items and refuted 3. Fixed before commit: message-only exceptions for calls that do not set a last error, opaque alpha pass, `SelectObject` and `GdiFlush` result checks, PerMonitorV2 thread scope in enumeration and capture, single pointer read in the pixel span, checked buffer sizes, defensive copy of the monitor list, `FrozenScreen` tests on a real DIB section, extra layout fixtures (primary in the middle, symmetric distance, stacked gap). Deferred with a note in D-036: cursor size on a mixed-DPI secondary monitor.

**Blocked or waiting on the owner**

- Merge PR #1 so `main` carries the docs. `milestone-1-capture` already contains those commits (D-024).
- Right-click the tray icon once and confirm Exit ends the process cleanly (the only path not covered by the automated check).
**Hardware tests the owner runs before the Milestone 1 PR is merged** (living checklist; steps add to it as they land; the owner does them in one sitting at the end). Build first with `dotnet build ScrubShot.sln -c Release` from the repo root; tool paths below are the Release outputs.

| # | Test | Command | Report back |
| --- | --- | --- | --- |
| H1 | Mixed-DPI capture | On the two-monitor setup (one at 125 or 150 percent): `tools\CaptureCheck\bin\Release\net10.0-windows10.0.26100.0\CaptureCheck.exe C:\Dev\scrubshot-hw\h1` | The printed monitor table (bounds, DPI, scale per monitor) and `Virtual screen` line; the pixel size of `composite.png` and each `monitor-N.png` (File Explorer > Properties > Details) compared with each monitor's native resolution; whether the composite shows both screens with no blur, no offset and no black band. |
| H2 | Cursor size on a secondary monitor | Move the mouse onto the non-primary monitor, then run `...\CaptureCheck.exe C:\Dev\scrubshot-hw\h2 --cursor` | Open `composite.png`: is the cursor present, at the right spot, and the same size as it looks on that monitor? If it looks one size too big or small, say which (D-036 limitation). |
| H3 | Exit from the tray menu | Run `src\ScrubShot.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\ScrubShot.exe`, right-click the tray icon, click Exit | The process disappears from Task Manager within a second. **Done 2026-11-01 (Debug build).** Repeat once on the final Release build. |
| H4 | Tray icon on light and dark taskbars | With the app running, switch Settings > Personalization > Colors between light and dark | The three-bubble icon is readable on both. |
| H5 | Region drag across the DPI boundary | `...\CaptureCheck.exe C:\Dev\scrubshot-hw\h5 --overlay region`, then drag a rectangle that starts on one monitor and ends on the other; release to confirm | The printed `Region:` line, and whether `selection.png` shows exactly what the bright rectangle showed, with no blur, offset or seam at the boundary. Also: did the dimmed overlay cover both monitors fully, and did the magnifier and label look the right size on each? |
| H6 | Window pick of a window straddling two monitors | Drag any app window so it spans both monitors, then `...\CaptureCheck.exe C:\Dev\scrubshot-hw\h6 --overlay window`, hover it (it highlights), click | The `Region:` line and whether `selection.png` is that window's full visible frame, crisp on both halves. |
| H7 | Keyboard-only selection | `...\CaptureCheck.exe C:\Dev\scrubshot-hw\h7 --overlay region`, do not touch the mouse: press Space, then Shift+Right five times and Shift+Down three times, then Enter | `Region:` should be 51x31 at the cursor position. If it is 6x4, Shift is not being detected on real hardware. |
| H8 | Hotkey conflicts and presses | Quit Greenshot (or any other screenshot tool) first. Run `tools\CaptureCheck\bin\Release\net10.0-windows10.0.26100.0\CaptureCheck.exe --hotkeys 15`, then within 15 s press Print Screen, Alt+Print Screen, Ctrl+Print Screen and Shift+Print Screen once each | The four binding lines (expected on Windows 11 with the default Accessibility setting: Region `OwnedBySnippingTool`, the other three `registered`) and the `Pressed:` lines that followed. Then turn off Settings > Accessibility > Keyboard > "Use the Print screen key to open screen capture", run the same command again and report whether Region is now `registered` and Print Screen produces `Pressed: Region`. |
| H9 | Tray menu and monitor submenu | Run `src\ScrubShot.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\ScrubShot.exe`, right-click the tray icon | The menu shows Capture region, Capture window, Capture full screen, Capture monitor (submenu with one entry per monitor and its pixel size, primary marked), Repeat last region, a separator, Open last capture and Settings greyed out, a separator, Exit. Click Capture region: the dimmed overlay appears on every monitor; drag and release; the overlay closes (output arrives in step 7). Click Capture monitor > the secondary monitor: no overlay, returns at once. |
| H10 | Second launch | With ScrubShot running, start the same exe again from Explorer | Task Manager shows one `ScrubShot.exe`; the second disappears within a second or two. |
| H11 | Launch at startup | With ScrubShot running, open Task Manager > Startup apps (or Settings > Apps > Startup) | A `ScrubShot` entry exists and points at the exe you ran. It stays after Exit (the setting defaults to on); remove it when testing is over, or turn the setting off once the Settings window lands in step 8. |

Later steps will add: full-screen and per-monitor captures at native resolution (checked through the output once step 7 lands), repeat last region after a layout change, Print Screen conflict dialog with Snipping Tool and the one-click fix, clipboard-history exclusion (Win+V shows nothing new), and no files on disk unless Save is chosen.

**Next**

1. Milestone 1 step 7: output sink (clipboard PNG and CF_DIB with history and cloud-sync exclusion, D-012; PNG or JPG save with the naming pattern; default action Copy, Save or both; local notification; Open last capture). Replaces `DiscardingCaptureSink` in the app. See `docs/milestone-1-plan.md`.
2. Step 8: minimal Settings window (General, Hotkeys with conflict status and the Snipping Tool fix, Capture), opened from the tray menu and from a second launch.
3. Hardware checklist above, run by the owner in one sitting before the PR.

**Spec files (decided by the owner later the same day).** `SPEC.md` in the repo is the public source of truth (public-safe edition of revision v1.1). The unedited spec with owner context is `CLAUDE.local-spec.md`, gitignored.

**Environment notes**

- Single 1920x1080 display at 100% on 2026-11-01. Mixed-DPI testing was not possible this session.
- `HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled` is not set on this machine; Snipping Tool 11.2608 is installed.
- `dotnet test` is in Microsoft.Testing.Platform mode (D-022). VSTest flags such as `--logger trx` are rejected; use `--report-xunit-trx`.
- Windows 11 hides new tray icons in the overflow area by default; use "Other system tray icons" in Taskbar settings to pin ScrubShot while developing.

**Open questions carried from the spec (do not block on them)**

- Ownership entity for the copyright line (decided by the owner outside the repo).
- Default risky AI site list and trusted app list for the clipboard monitor.
- Which NER model for names and addresses, given size and license.

## 2026-11-01 (session 1)

**Milestone:** 1 (foundation and capture). Not started. Plan proposed in `docs/milestone-1-plan.md`, awaiting owner approval. No code has been written, by design: the spec asks for the structure and plan to be proposed first.

**Done**

- Public repo created at <https://github.com/emichael14/scrubshot>, GPL-3.0-only.
- PR #1 (`chore/repo-setup` into `main`) open with: README, `docs/decisions.md` (D-001 to D-020), `docs/milestone-1-plan.md`, `docs/architecture.md`, `SECURITY.md`, issue templates (bug, feature, detection gap), `.gitignore`, `.gitattributes`, `CLAUDE.md`, this file.
- GitHub private vulnerability reporting enabled on the repo.
- Build machine audited (see plan). Microsoft Docs plugin for Claude Code installed; it loads in the next session.

**Blocked or waiting on the owner**

- .NET 10 SDK is not installed on the dev PC. Needs `winget install Microsoft.DotNet.SDK.10` in an elevated terminal.
- Approval of the Milestone 1 plan and its five stated assumptions. Merging PR #1 counts as approval.
- PR #1's description was generated from the first commit only and does not describe the later revisions. Update if wanted.
- Memory proposal pending: project location and status; use the Write tool for long files in this harness.

**Next**

1. Owner installs the SDK and merges PR #1 (or says go).
2. `dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates`; confirm `dotnet new list winui`.
3. `git checkout main && git pull && git checkout -b milestone-1-capture`.
4. Milestone 1 step 1: scaffold (solution, props, `global.json`, `.editorconfig`, Core, Capture, App, Tests, CI, SPDX headers). App shows a tray icon and exits cleanly; build and tests green locally and in CI.

**Environment notes**

- Single 1920x1080 display at 100% on 2026-11-01. The mixed-DPI acceptance test needs a second display or the owner's two-monitor setup.
- Windows App Runtime 1.2 through 2.6.4 present. No Visual Studio, no Windows SDK; the plan uses the documented Visual Studio-free path.
- `HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled` is not set on this machine; Snipping Tool 11.2608 is installed.
