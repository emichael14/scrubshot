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

**Why.** The ownership entity (Technest Valley LLC or a separate ScrubShot LLC) is an open question in the spec. SPDX keeps headers short and machine-readable, and the placeholder is one search-and-replace away from final.

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
