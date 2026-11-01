# Decisions log

Every decision not covered by the build spec gets an entry here: what was decided, why, and what was considered instead. Newest entries at the bottom. When a decision is reversed, add a new entry that references the old one rather than editing history.

Guiding rule from the spec: when in doubt, choose the more private and more configurable option.

---

## 2026-11-01

### D-001 Target .NET 10 (LTS)

**Decision.** Build on .NET 10. Target framework `net10.0-windows10.0.26100.0`. Pin the SDK in `global.json` to the 10.0.4xx band with `rollForward: latestFeature`.

**Why.** .NET 10 is the current LTS (released 2025-11-11, supported until 2028-11-14). .NET 11 ships around 2026-11-10 as a Standard Term Support release; the shorter support window is the wrong trade for an enterprise-deployed tool.

### D-002 Windows App SDK 2.x with WinUI 3

**Decision.** Use the Windows App SDK 2.x stable line (2.6.4 at the time of writing, released 2026-10-27) for WinUI 3, Mica, AppNotification and related APIs.

**Why.** Specified. 2.x is the current stable channel, serviced until 2027-04-29. The 1.8 line is in maintenance.

### D-003 Minimum OS is Windows 10 version 2004 (build 19041); Windows 11 is the tested platform

**Decision.** `TargetPlatformMinVersion` = 10.0.19041.0.

**Why.** It costs nothing, keeps Windows 10 fleets on Extended Security Updates in scope, and 19041 is the floor for capture APIs we may adopt later (borderless Windows.Graphics.Capture). Mica and acrylic fall back to solid backgrounds on Windows 10. Only Windows 11 is tested during development.

### D-004 Unpackaged, self-contained deployment is primary; MSIX is a secondary package

**Decision.** The app runs without package identity by default. Windows App SDK is deployed self-contained (`WindowsAppSDKSelfContained=true`) and .NET is published self-contained. MSIX is produced from the same project in Milestone 6 as an additional artifact.

**Why.** Global hotkeys, the tray icon, the clipboard listener and HKLM policy reads are simplest and most predictable without package identity. The portable build requires it. Self-contained runtimes mean the installer and the portable zip work offline with no prerequisites, which matters for locked-down enterprise machines.

**Considered.** MSIX only. Rejected: breaks the portable build and complicates Win32 interop.

### D-005 Capture engine: GDI BitBlt per monitor behind an `IScreenCapturer` seam

**Decision.** Capture each monitor with BitBlt from the screen device context at physical-pixel coordinates into one composite in-memory bitmap. The process is declared PerMonitorV2 DPI aware in its manifest. Windows.Graphics.Capture is implemented later behind the same interface.

**Why.** BitBlt at physical coordinates is the simplest path that is pixel-exact on mixed-DPI layouts, and it is the approach Greenshot and ShareX rely on. DRM-protected content captures as black, which is acceptable and arguably desirable for this product.

**Considered.** Windows.Graphics.Capture first. Deferred: needs a D3D device, per-item sessions and a yellow border unless borderless access is granted; more moving parts for no Milestone 1 benefit. It becomes valuable for HDR displays and occluded windows.

### D-006 The selection overlay is native Win32, one topmost window per monitor, not XAML

**Decision.** On hotkey, create one borderless topmost Win32 window per monitor, each at that monitor's DPI, drawing that monitor's slice of the frozen bitmap 1:1 with double-buffered GDI. A shared virtual-screen selection model lets one drag span monitors. Rendering sits behind a small renderer interface so Direct2D can replace GDI later.

**Why.** XAML scales content by the window's rasterization scale, which fights a 1:1 frozen bitmap on mixed-DPI setups. Per-monitor Win32 windows are instant to show and exact by construction. The spec explicitly allows Win32 interop for the capture overlay.

**Considered.** A single window spanning the virtual screen (simpler cross-monitor drag, but DWM sizing quirks are reported with mixed DPI). A WinUI window (DPI scaling conflicts, slower to appear).

### D-007 Add a `ScrubShot.Capture` project to the spec's layout

**Decision.** Monitor enumeration, capturers, the overlay and capture geometry live in `src/ScrubShot.Capture`, which depends on Win32 only, not on WinUI. `ScrubShot.App` keeps the tray, menus, editor and settings UI. Projects live under `src/` and `tests/`.

**Why.** Keeps the capture engine testable headless and lets a small `CaptureCheck` console tool exercise it on real mixed-DPI hardware for pixel-accuracy checks. It also gives the seam for alternate capturers (D-005).

### D-008 P/Invoke through Microsoft.Windows.CsWin32 (MIT)

**Decision.** All Win32 signatures are generated from `NativeMethods.txt` by CsWin32. No hand-written `DllImport`.

**Why.** Generated, correct signatures with no drift. Source generator only; nothing ships at runtime. MIT licensed, no network.

### D-009 Tray icon through H.NotifyIcon.WinUI (MIT)

**Decision.** Use H.NotifyIcon.WinUI for the tray icon and its menu.

**Why.** WinUI 3 has no tray API. The library wraps Shell_NotifyIcon and integrates WinUI flyouts. MIT licensed, no network calls. If it misbehaves, fall back to our own Shell_NotifyIcon interop via CsWin32.

### D-010 MVVM through CommunityToolkit.Mvvm (MIT)

**Decision.** Observable objects, relay commands and messaging come from CommunityToolkit.Mvvm.

**Why.** Standard, source-generated, MIT licensed, no runtime dependencies beyond itself.

### D-011 Settings and policy layer built in Milestone 1 in Core; full Settings UI in Milestone 5

**Decision.** `ScrubShot.Core` ships the settings model, a versioned JSON store at `%LOCALAPPDATA%\ScrubShot\settings.json`, and a policy resolver that reads `HKLM\Software\Policies\ScrubShot` then `HKCU\Software\Policies\ScrubShot`. Policy value names mirror setting paths (for example `Hotkeys.Region`). Resolution returns the value and its source (MachinePolicy, UserPolicy, User, Default) so the UI can show a lock. Milestone 1 includes a minimal Settings window (General, Hotkeys, Capture) covering only the settings Milestone 1 uses.

**Why.** "Everything configurable and policy-enforceable" is cheaper to honor from the first setting than to retrofit. The spec lists the full Settings window under Milestone 5, but hotkeys and save location must be changeable in Milestone 1 for the app to be usable.

### D-012 Clipboard-history and cloud-sync exclusion ships in Milestone 1

**Decision.** Clipboard writes set the documented formats `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory` = 0 and `CanUploadToCloudClipboard` = 0 when the setting (default on) is enabled.

**Why.** Milestone 1 already writes captures to the clipboard. "Safe by default" means the exclusion should ship with the first clipboard write, not three milestones later. The spec lists it under Milestone 4; this pulls it forward.

### D-013 Print Screen conflict detection and one-click fix

**Decision.** A failed `RegisterHotKey` (ERROR_HOTKEY_ALREADY_REGISTERED) reports a conflict for any hotkey. For Print Screen, ScrubShot additionally reads `HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled` to name the Windows Snipping Tool as the likely owner. The one-click fix sets that value to 0 (user hive, no elevation) and re-registers. ScrubShot never changes it without the user clicking the fix.

**Why.** Snipping Tool owning Print Screen is the most common conflict on Windows 11 and the registry switch is the documented control for it.

### D-014 Logging is content-free by construction

**Decision.** Logging goes through Microsoft.Extensions.Logging abstractions with an opt-in local file sink. Log call sites take event ids and numeric, boolean or timing fields only. There is no helper that accepts captured text, capture file names or detected values.

**Why.** Non-negotiable in the spec. Making the unsafe call impossible to write is stronger than reviewing for it. The "no sensitive data in logs" test (Milestone 3) additionally scans log output for fixture values.

### D-015 SPDX license headers with a placeholder copyright line

**Decision.** Every source file starts with:

```
// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided; see docs/decisions.md.
```

**Why.** The ownership entity for the copyright line is not yet decided (an owner matter outside the repo). SPDX keeps headers short and machine-readable, and the placeholder is one search-and-replace away from final.

### D-016 Repo bootstrapped locally; `main` holds only what GitHub's template would have created

**Decision.** The GitHub repository could not be created from the autonomous build session (creating a public surface requires owner approval). The local repo was initialized with a single commit on `main` containing the GPL-3.0 `LICENSE` and a README stub, mirroring what `gh repo create --license gpl-3.0 --add-readme` generates. Everything else arrives through branches and pull requests.

**Why.** Keeps the "never commit directly to main" convention intact apart from the unavoidable initial commit, and lets the owner create the remote and push with one command.

### D-017 Default values chosen for Milestone 1 settings

| Setting | Default | Reason |
| --- | --- | --- |
| Launch at startup | on | A tray capture tool that is not running is not a capture tool. Installer offers to turn it off. |
| Default action after capture (until the post-capture menu exists) | Copy | Fastest path; nothing written to disk unless asked. |
| Save folder | `%USERPROFILE%\Pictures\ScrubShot` | Conventional, user-owned. |
| File name pattern | `${YYYY}-${MM}-${DD}_${hh}-${mm}-${ss}` | Sorts chronologically; no window titles by default, which can leak context. |
| Format | PNG | Lossless; JPG available with quality setting. |
| Capture mouse cursor | off | Cursors rarely belong in documentation screenshots. |
| Capture delay | 0 ms | |
| Magnifier and crosshair coordinates | on | Spec lists them as toggles; on helps precision. |
| Exclude clipboard writes from history and cloud sync | on | Protective settings default on. |
| Debug logging | off | Opt-in only. |

### D-018 CLA Assistant moves from Milestone 6 to Milestone 1

**Decision.** The contributor license agreement and its automated check ship with Milestone 1. Preferred implementation is the CLA Assistant GitHub Action (`contributor-assistant/github-action`), which keeps signatures in the repository; the hosted cla-assistant.io service is the fallback if the action proves awkward.

**Why.** Owner request. The repo is public from day one and the README invites bug reports and ideas now; code contributions should open as early as possible without giving up dual-licensing rights, and that requires the CLA to exist before the first external pull request.

### D-019 Public claims are framed as commitments until they are implemented and tested

**Decision.** The README states privacy and admin-policy behaviors as design commitments and says so explicitly, and carries a Limitations section (detection is a safety net, OCR can miss or misread, review before sharing, not a replacement for DLP). As each behavior ships with tests, the wording moves from intent to verified fact. The README stays plain and impersonal: no author material and nothing about how the project is built.

**Why.** Owner request. Overstating protection in a security tool is worse than understating it; users and security teams need to know what is verified today.

### D-020 License is GPL-3.0-only, not GPL-3.0-or-later

**Decision.** The project is licensed under GPL-3.0-only. SPDX identifier `GPL-3.0-only` in source headers (D-015 updated), README and package metadata. The `LICENSE` file is the unmodified GPLv3 text, which is the same for either variant.

**Why.** Owner decision on 2026-11-01. Terms stay fixed to GPLv3 as written rather than deferring to future license versions. With the CLA granting the owner relicensing rights, "or later" adds no flexibility the project needs.

**Considered.** GPL-3.0-or-later: slightly broader compatibility with future GPL versions, at the cost of binding to terms not yet written.

---

## 2026-11-01 (Milestone 1, step 1: scaffold)

### D-021 One target framework for every project

**Decision.** All projects target `net10.0-windows10.0.26100.0` with `TargetPlatformMinVersion` 10.0.19041.0, set once in `Directory.Build.props`. The plan had `net10.0-windows` for Core and Capture.

**Why.** A plain `net10.0-windows` project cannot reference a library built for a 10.0.x platform version, so the test project would have forced the choice anyway. Capture will want WinRT APIs (Windows.Graphics.Capture) behind `IScreenCapturer`, which need the platform TFM. One value in one file cannot drift.

### D-022 Tests run on xUnit v3 over Microsoft.Testing.Platform

**Decision.** `xunit.v3` 4.x only, `OutputType=Exe`, `UseMicrosoftTestingPlatformRunner=true`. `global.json` selects `"test": { "runner": "Microsoft.Testing.Platform" }`. No `Microsoft.NET.Test.Sdk`, no `xunit.runner.visualstudio`. CI and local runs use `dotnet test --report-xunit-trx --results-directory TestResults`.

**Why.** xunit.v3 4.x defaults to MTP v2, and the .NET 10 SDK refuses to run MTP projects through the legacy VSTest path (`dotnet test` fails with an explicit error). MTP is the SDK's documented direction and is faster.

**Considered.** xunit 2.9 on VSTest, which is what the SDK's `dotnet new xunit` template still emits. Rejected as the legacy path.

### D-023 License header wording and enforcement

**Decision.** The second header line is now `Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).` (supersedes the wording in D-015). `.editorconfig` carries the header as `file_header_template` and IDE0073 is a warning, which `TreatWarningsAsErrors` plus `EnforceCodeStyleInBuild` turns into a build failure for any `.cs` file without it. XAML and manifest files carry the same two lines as XML comments at the top; the analyzer does not cover them.

**Why.** Roslyn's `.editorconfig` parser treats `;` and `#` as inline comment starters, so the original "decided; see docs/decisions.md" was truncated and every file failed the header check. Enforcing at build time is cheaper than reviewing for it.

### D-024 Milestone branch cut from `chore/repo-setup` while PR #1 is open

**Decision.** `milestone-1-capture` was created from `chore/repo-setup` (the head of PR #1), not from `main`.

**Why.** `main` holds only the initial commit until PR #1 merges, so a branch from it would lack `CLAUDE.md`, the docs and the plan. `chore/repo-setup` is exactly what `main` becomes after the merge; the extra commits disappear from the Milestone 1 diff once PR #1 lands.

### D-025 App platforms and publish settings

**Decision.** `ScrubShot.App` builds for x64 and ARM64 only. `PublishTrimmed` is off; `PublishReadyToRun` is on for non-Debug builds. The template's `Microsoft.Windows.SDK.BuildTools.WinApp` package (packaged-app `dotnet run` support) is not referenced.

**Why.** Windows 11 has no 32-bit edition and 32-bit Windows 10 is rare in the target audience. Trimming a WinUI 3 app with a third-party XAML library needs verification that belongs in Milestone 6 (packaging), not in the scaffold. The WinApp build-tools package only serves packaged apps; the primary deployment is unpackaged (D-004).

### D-026 Allowed parameter types for the logging API (refines D-014)

**Decision.** Methods on `ScrubShot.Core.Logging.Log` may take only `ILogger` (first), `bool`, `int`, `long`, `double`, `TimeSpan`, `Version` and enums. `string`, `object`, `Exception` and buffers are excluded. `tests/ScrubShot.Tests/Core/LogApiTests.cs` enforces this by reflection. Errors will be logged as error codes (HResult or Win32 error) through a helper that arrives with the opt-in file sink.

**Why.** Exception messages routinely embed file paths and user data (an `IOException` from a save names the capture file), so allowing `Exception` would reopen the hole D-014 closes.

### D-027 The app verifies PerMonitorV2 at startup

**Decision.** `ScrubShot.Capture.ProcessDpi.IsPerMonitorV2()` compares the thread's `DPI_AWARENESS_CONTEXT` with `DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2` through `AreDpiAwarenessContextsEqual`. The app logs event 1002 (warning) if the manifest did not take effect. Capture still runs.

**Why.** Every coordinate in the pipeline is a physical pixel (D-005). If the manifest is ever lost (a packaging or launcher change), scaled captures should show up in a debug log rather than as a silent accuracy bug.

### D-028 App icon: original bubble trio, two SVG sources, generated ICO and PNGs

**Decision.** The icon is original artwork chosen by the owner on 2026-11-01 from five candidate directions: three glossy soap bubbles stepping diagonally from large to small, aqua/teal fills with a dark teal outline and one highlight per bubble. Sources are `assets/icon/scrubshot.svg` (32 px and up) and `assets/icon/scrubshot-small.svg` (16, 20 and 24 px: the same three bubbles, enlarged, outline 20 units, highlights snapped to a 16-unit grid). `tools/IconBuild` (Svg.Skia, MIT, development only) renders both into `scrubshot.ico` (16, 20, 24, 32, 48, 64 as 32-bit DIBs with an AND mask; 256 as PNG), PNGs from 16 to 512 and a light/dark preview sheet. `ScrubShot.App` embeds the `.ico` through `ApplicationIcon` and ships a copy as `Assets\scrubshot.ico` for the tray icon (`ms-appx:///Assets/scrubshot.ico`). The outline color is `#0D4A45`, a shade of the palette outline, so the rim stays visible on a dark taskbar.

**Why.** The owner asked for three bubbles to remain visible in the tray, which rules out the usual two-element simplification; the small variant keeps all three by filling the tile and thickening the outline. Generated raster output from two vector sources means every size is reproducible and the tray and app icons cannot drift. A faint light halo behind the bubbles (suggested for dark taskbars) was tried and rejected because it blurred the edge on light taskbars while the bright fills already separate the bubbles on dark ones.

**Considered.** A hero bubble with satellites and a rising four-bubble curve (both shortlisted; lower legibility at 16 px), a five-bubble foam cluster (blobs at 16 px) and a four-bubble frame hint (read as a 2x2 grid close to the four-pane Microsoft shape). Hand-drawn PNGs per size: rejected as unmaintainable.

### D-029 Two-layer detection: a small local model is core, not an add-on (spec revision v1.1)

**Decision.** Owner decision on 2026-11-01, recorded as spec revision v1.1. Detection runs two layers over the same OCR text: layer 1 is the rule detectors (regex, validators, entropy, context labels); layer 2 is a small local named-entity / PII recognition model run through ONNX Runtime on the CPU. Both produce hits with confidence scores that merge into one set of scrub regions; sensitivity profiles decide what is scrubbed. The model ships with the app, is on by default, and can be turned off by the user setting and admin policy `Scrubbing.AiLayerEnabled`; layer 1 keeps running when it is off. Names and addresses leave the add-on list. Model requirements: offline and CPU-only on a 4-core, 8 GB laptop; about 200 ms or less added per typical snip; quantized and under roughly 100 MB; license compatible with GPL-3.0-only (Apache 2.0 or MIT preferred), no usage restrictions, no phoning home; custom entity types without retraining preferred. Before Milestone 3 starts, 2 or 3 candidates are benchmarked against the detection corpus (precision, recall, latency, size) next to a rules-only baseline and the owner picks one. Plan in `docs/milestone-3-notes.md`.

**Why.** Rules only catch what has a shape or a label. The leaks that hurt most in practice (a person's name in a ticket, an address on a form, a secret pasted without a label) have neither, and leaving that to an optional add-on means most installs never get it. Keeping the model switchable by policy preserves the "everything configurable, everything enforceable" rule and gives admins who distrust on-device models a way out.

**Considered.** Model as an optional add-on (the original spec): rejected by the owner for the coverage reason above. Cloud or GPU inference: excluded by the local-only and modest-hardware requirements.

### D-030 License audit on 2026-11-01: GPL-3.0-only everywhere, nothing to fix

**Decision.** After PR #1 merged, every license mention was audited: README, `LICENSE` (unmodified GPLv3 text), SPDX headers in all `.cs`, `.xaml`, `.svg` and manifest files, the `.editorconfig` header template, `Directory.Build.props` package metadata, `CLAUDE.md`, `assets/icon/README.md` and `docs/status.md`. All say GPL-3.0-only. The only "or-later" strings are inside D-020, which records the choice. No change was needed; this entry records that the check was done so it is not repeated.

### D-031 Settings schema is derived from the record shape; policy merges through JSON

**Decision.** `ScrubShotSettings` is a tree of immutable records (sections General, Output, Capture, Hotkeys, Advanced) whose leaf properties are the settings. `SettingsSchema` derives the list of leaf paths ("Section.Name"), types and defaults from that shape by reflection, once. `SettingsResolver` serializes the user settings to JSON, overlays policy values from the user hive and then the machine hive onto the matching JSON leaves, and deserializes the result; it records a `SettingSource` (Default, User, UserPolicy, MachinePolicy) per path, and `EffectiveSettings.IsLocked` is true for the two policy sources. The policy registry value name is the setting path, so `Output.DefaultAction` in `HKLM\Software\Policies\ScrubShot` sets that setting. `SchemaVersion` is not a setting and cannot be set by policy.

**Why.** One declaration (the record) feeds the file format, the policy names, the lock state and, in Milestone 5, the ADMX templates, so none of them can drift. Records with `init` properties keep settings immutable in flight, which suits MVVM and makes "re-resolve when something changes" trivial. The JSON overlay avoids a hand-maintained switch over every setting.

**Considered.** A hand-written `SettingDefinition<T>` list with getters and setters: explicit but duplicates every property. A flat dictionary of settings: loses typing for consumers.

### D-032 Policy value conversion rules

**Decision.** Policy sources yield only REG_DWORD (int) and REG_SZ or REG_EXPAND_SZ (string, environment variables not expanded) values; other kinds are dropped, and a value that fails to read (a policy refresh rewriting the key) is skipped without dropping the rest of the hive. Conversion to the setting type: bool from DWORD (non-zero is true) or the strings `true`, `false`, `1`, `0`; int from DWORD or an invariant-culture integer string; enum from an exact member name (case-insensitive; comma lists and numeric strings are rejected) or a DWORD ordinal that is defined; string from REG_SZ only. A value that names an unknown setting or fails conversion is ignored, the lower-precedence value stays, and only the count of ignored values is logged (event 1104). Range validation of ints (for example JPEG quality) belongs to the consumer, not the resolver.

**Why.** Group Policy and Intune write DWORDs for booleans and strings for everything else, so both forms must work. Ignoring bad values rather than failing keeps a policy typo from breaking the app, and leaving the lower-precedence value in place means a bad machine value can never unlock a valid user-policy value. Logging only a count keeps policy names and values out of logs (D-014).

### D-033 Settings file handling: atomic replace, corrupt file set aside, no writes on load

**Decision.** `JsonSettingsStore` writes to a temporary file in the same folder, flushes to disk, then moves it over `settings.json` with overwrite, so a crash mid-write leaves the previous file intact. Load never creates the folder or file. The document is parsed to a JSON tree first and shaped to the schema by `SettingsDocument.Normalize` before binding: property names are matched case-insensitively, absent sections and leaves are filled with defaults silently, and a leaf that is present but null, of the wrong JSON kind or an undefined enum name is reset to its default and counted (event 1105), so one bad value never discards the file. Only a file that is not valid JSON is set aside, under a timestamped `settings.invalid.<stamp>.json` name that never overwrites an earlier copy. A file that could not be read (locked, access denied) or a corrupt file that could not be set aside is set aside before the next save; if that fails, the save throws rather than overwrite. A file written by a newer schema is copied to `settings.v<N>.json` before this build first rewrites it. Older schema versions are migrated in memory on load (schema 1 is the first, so migration is a version bump). Enums are stored as names, properties are PascalCase, comments and trailing commas are tolerated when reading.

**Why.** Users and admins edit this file by hand and sync it between machines; overwriting a broken one silently would destroy their edits, while refusing to start would be worse. Per-leaf reset keeps everything that is good, and the set-aside and backup copies make every destructive path recoverable. These rules came out of the adversarial review of the first version, which found that a single bad leaf, an explicit `null`, a locked file or a newer file could each lose user data.

### D-034 File name collisions get a numeric suffix

**Decision.** When a target file exists, `UniqueFileName` appends `-2`, `-3`, ... before the extension, up to 10,000 attempts. The `${NUM}` token in patterns is a separate sequence number supplied by the caller and is not used for collision handling.

**Why.** A suffix keeps the chosen pattern intact and sorts next to the original. Reusing `${NUM}` for collisions would make the pattern's meaning depend on what is already on disk.

### D-035 Pixel geometry lives in Core; the monitor layout is a plain model

**Decision.** `ScrubShot.Core.Geometry` holds `PixelPoint`, `PixelSize`, `PixelRect` (origin plus size, exclusive right and bottom edges like Win32 `RECT`, non-negative sizes enforced) and `MonitorInfo` / `MonitorLayout` (bounds, work area, DPI and primary flag per monitor; the virtual screen as the union of bounds; conversions between virtual-screen, bitmap and monitor-local coordinates). `ScrubShot.Capture.MonitorEnumerator` fills the model from `EnumDisplayMonitors`, `GetMonitorInfo` and `GetDpiForMonitor`; a layout rejects zero monitors, zero or several primaries, non-sequential indexes and empty bounds.

**Why.** Every coordinate decision (which monitor a point is on, where a monitor lands in the frozen bitmap, clamping a drag) is pure arithmetic that must be right on mixed-DPI hardware the development machine does not have. Keeping it free of Win32 lets synthetic layouts (150 percent laptop beside a 100 percent monitor, negative origins, stacked monitors) cover it in unit tests that run in CI.

### D-036 The frozen screen is one DIB section, cleared on dispose

**Decision.** `FrozenScreen.Allocate` creates a single 32-bit top-down DIB section the size of the virtual screen (rejecting layouts whose byte size would not fit an int); `GdiScreenCapturer` selects it into a memory DC and fills it with one `BitBlt` (`SRCCOPY | CAPTUREBLT`) per monitor from the screen device context at physical coordinates, checks `SelectObject` and `GdiFlush` results so a failed blit can never come back as a silent black capture, then sets every alpha byte to 0xFF because GDI leaves the fourth byte of a 32-bit `BI_RGB` surface undefined. Both `MonitorEnumerator.GetLayout` and `Capture` switch the calling thread to PerMonitorV2 (`SetThreadDpiAwarenessContext`) for their duration and restore it, so a thread in another context cannot produce scaled coordinates. `FrozenScreen` owns the `HBITMAP` and the pointer to its bits, exposes the pixels as a read-only span that must not outlive it and `Crop` for tightly packed copies, keeps the GDI handle for the overlay to blit from, and on dispose zeroes the pixel memory before deleting the bitmap (a finalizer does the same if an owner forgets). Device contexts are created and released inside the capture call, never kept, so the bitmap can be used from any thread. Exceptions carry a Win32 error code only for calls documented to set one (`BitBlt`, `CreateDIBSection`, `GdiFlush`). The cursor is composited on request from `GetCursorInfo` and `DrawIconEx` at its hotspot and resource size, and the `ICONINFO` bitmaps are deleted. The capture delay is the coordinator's job, not the capturer's.

**Why.** One contiguous buffer is the simplest thing that is pixel-exact across monitors and gives both GDI (overlay) and managed code (OCR, encoding) direct access without a copy. Zeroing on dispose honors "the original lives only in memory for the session" in the strongest practical sense. A DIB section is pagefile-backed like any other memory; nothing is written to a file. The result checks, the alpha pass, the thread DPI scope and the error-code rule came out of the adversarial review of the first version.

**Known limitation.** The cursor is drawn at the size of the cursor resource Windows handed back, not scaled to the DPI of the monitor it is on, so on a mixed-DPI secondary monitor it may be one size step off. Fixing it needs the mixed-DPI hardware test to validate the scale factor (candidate: `GetSystemMetricsForDpi(SM_CXCURSOR, monitor DPI)` against the resource size); revisit at the acceptance test.

### D-037 CaptureCheck ships with the capture engine, not at the end

**Decision.** `tools/CaptureCheck` (console, PerMonitorV2 manifest) was built in step 3 rather than step 9 of the plan. It prints the monitor table, captures through the real engine and writes the composite and per-monitor PNGs to a folder the user names, encoding with `Windows.Graphics.Imaging.BitmapEncoder` (in-box, no package). On the development machine it produced a correct 1920x1080 capture with the cursor in 51 ms.

**Why.** It is the only way to see the engine's output before the overlay exists, and the same PNG path is what the app's save feature will use later. It is a development tool and is the one place outside the app that writes captured pixels to disk, by explicit user request.

### D-038 Selection overlay: shared model in Core, GDI windows per monitor, keyboard-complete

**Decision.** `ScrubShot.Core.Selection.RegionSelection` is the state machine (Idle, Dragging, Selected) shared by every overlay window: inclusive rectangles (a drag from (10, 10) to (12, 12) selects 3x3; a click without movement selects nothing), clamping to the virtual screen, nudging of the far corner, `Select` for window picks and repeat-last-region, `LastRegion.Reconcile` and `WindowPick.HitTest` alongside it. `ScrubShot.Capture.Overlay.OverlaySession` creates one `WS_POPUP | WS_EX_TOPMOST | WS_EX_TOOLWINDOW` window per monitor at its physical bounds, each with a dimmed copy of its slice (45 percent) and a back buffer; the bright selection is blitted from the frozen screen's own bitmap, with a 1 px accent border, a crosshair, a label (selection size and cursor coordinates, both toggles) and a 4x nearest-neighbor magnifier (M toggles). Mouse positions come from the message's lParam plus the window origin, never from `GetCursorPos` at processing time, so a fast drag cannot race. Input: left drag (release confirms by default, a setting can require Enter), Space or Enter anchors from the keyboard and then confirms, arrows nudge by 1 px or 10 px with Shift, Esc or right-click cancels. Window mode hit-tests `WindowFinder` candidates (visible, not minimized, not cloaked, not tool windows, not the desktop shell, not the overlay's own thread) front to back and confirms on click or Enter. The loop runs on a dedicated STA thread via `RunOnNewThreadAsync`; `CaptureCheck --overlay region|window` exercises it, and a scripted drag, cancel, keyboard-only selection and window pick all returned the expected regions on the development machine.

**Why.** The spec allows Win32 for the overlay and wants it instant; GDI from a prepared bitmap is as fast as it gets and has no DPI scaling to fight (D-006). One model for all windows is what makes a cross-monitor drag trivial. Taking positions from the message rather than polling fixed a real off-by-pixels anchor seen in testing. Keyboard-only selection satisfies "keyboard first".

**Known limitations.** Confirmed only on a single 100 percent monitor so far; cross-DPI drag and a window straddling two monitors are on the owner's hardware checklist. The magnifier and label sizes follow the primary monitor's DPI, not the monitor under the cursor.

