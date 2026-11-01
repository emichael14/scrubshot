# ScrubShot

**A free, open-source screenshot tool for Windows that scrubs sensitive data before it leaves your PC.**

ScrubShot gives you the full Greenshot-style capture and annotation workflow with a clean Windows 11 look, plus one layer built for the AI era: every capture is OCR'd locally, and anything sensitive (SSNs, EINs, card numbers, Microsoft tenant and client IDs, client secrets, API keys, connection strings, emails and more) is detected and blurred automatically before the image reaches the clipboard or disk. An optional clipboard monitor does the same for text the moment you paste into a risky destination such as a public AI chat site.

Built for IT professionals and employees in regulated industries whose companies want them to use AI tools without leaking company data.

> **Status: pre-alpha.** Milestone 1 (foundation and capture) is in progress. Nothing is ready to install yet. See the [roadmap](#roadmap).

## Privacy promises

These are design constraints, not settings:

- **Everything runs locally.** No telemetry, analytics, crash reporting or cloud APIs. The only network call ScrubShot will ever make is an optional, configurable update check against GitHub Releases, and admins can turn it off. Block ScrubShot at the firewall and it keeps working.
- **Unscrubbed images never touch disk.** The original capture lives in memory for the life of an editor session and is cleared when the session ends. Copy and Save output only the scrubbed result.
- **Logs never contain captured content.** Opt-in debug logs record events, timings, versions and errors. Never text, images, file names of captures or detected values. A test fails the build if that changes.
- **Safe by default.** Default sensitivity is Strict. Protective settings default on.
- **Every setting is admin-enforceable** through Windows policy (`HKLM\Software\Policies\ScrubShot` and `HKCU\Software\Policies\ScrubShot`) with ADMX/ADML templates, so Intune, Group Policy or any MDM can manage and lock it.

## What it will do

**Capture.** Region, window, full screen, specific monitor and repeat-last-region, with Greenshot-style hotkeys (Print Screen, Alt, Ctrl and Shift variants, all remappable). A frozen, dimmed overlay keeps menus and tooltips visible while you select. Magnifier and crosshair coordinates. Pixel-accurate on mixed-DPI multi-monitor setups.

**Post-capture menu.** Copy, Open in Editor, Save, Print with single-key shortcuts. Reorder, hide, remap, or skip the menu and always run a default.

**Editor.** Crop, resize, rotate, shapes, arrows, freehand, text, speech bubbles, step counters, highlighters, manual blur and pixelate, effects (shadow, border, torn edge). Every annotation stays editable. Full undo and redo.

**Auto-scrub.** Local OCR plus a detector pipeline for personal data, financial data, Microsoft identifiers and secrets, third-party API tokens and credentials. Context labels ("Tenant ID", "Secret", "Key") raise confidence. Sensitivity levels Strict, Balanced, Relaxed and Custom. A side panel lists each scrubbed item with a masked preview and a keep/reveal toggle. Redaction styles: blur, pixelate, solid block, labeled block.

**Clipboard monitor (optional).** Scrubs text based on where you paste, not when you copy. Risky and trusted destination lists, per-type switches, silent/notify/ask behaviors.

**Add-ons (optional, local).** Name and address detection, face detection, QR and barcode detection, file and PDF scrubbing, image metadata stripping.

## How a capture flows

Capture all monitors into memory → frozen overlay → region/window/full selection → crop in memory → OCR (words + boxes) → detectors run on text and image → merge overlapping hits → apply sensitivity profile → produce scrub regions → render scrubbed image → Copy, Editor, Save or Print.

## Solution layout

| Project | Purpose |
| --- | --- |
| `src/ScrubShot.App` | WinUI 3 app: tray, post-capture menu, editor, settings, toasts |
| `src/ScrubShot.Core` | Shared models, settings, policy resolution, logging abstractions, geometry |
| `src/ScrubShot.Capture` | Win32 screen capture, monitor and DPI handling, selection overlay (no UI framework) |
| `src/ScrubShot.Detection` | OCR wrapper, detector pipeline, detectors, sensitivity profiles (no UI references) |
| `src/ScrubShot.Clipboard` | Clipboard monitor, paste-destination detection, text scrubbing |
| `src/ScrubShot.AddIns` | Add-on contract and add-ons |
| `tests/ScrubShot.Tests` | Unit and fixture tests, detection corpus |
| `deploy/` | Installer, Intune package, ADMX/ADML templates, sample config |
| `docs/` | Decisions log, detection catalog, admin guide, milestone plans |

## Building from source

No Visual Studio required. Target: Windows 11 (runs on Windows 10 2004 and later).

```powershell
winget install Microsoft.DotNet.SDK.10
git clone https://github.com/emichael14/scrubshot.git
cd scrubshot
dotnet build
dotnet test
dotnet run --project src/ScrubShot.App
```

The solution lands with Milestone 1. Until then these commands are the target, not the current state.

## Roadmap

Each milestone ends with a working, runnable app, passing tests and an updated README.

- [ ] **1. Foundation and capture.** Tray app, hotkeys with conflict detection, frozen overlay, all capture modes, mixed-DPI correctness, copy and save. ([plan](docs/milestone-1-plan.md))
- [ ] **2. Post-capture menu and editor.** Customizable menu, full editor toolset, effects, undo/redo, print.
- [ ] **3. Auto-scrub engine.** OCR, core detectors, context labels, sensitivity levels, side panel, redaction styles, review screen.
- [ ] **4. Clipboard monitor.** Paste-destination detection, risky/trusted lists, per-type switches.
- [ ] **5. Settings, policy and add-ons.** All settings tabs, ADMX templates, registry policy with locks, add-on framework.
- [ ] **6. Packaging and launch.** Installer, silent install, Intune recipe, portable build, code signing, update modes, admin guide.

## Project documents

- [Decisions log](docs/decisions.md): every design decision not covered by the spec, with reasons.
- [Milestone 1 plan](docs/milestone-1-plan.md)

## Contributing

Contributions are welcome once Milestone 1 is merged. A contributing guide and contributor license agreement are planned for Milestone 6. Bug reports can be filed now using the issue templates. Please never attach screenshots containing real sensitive data.

## License

GPL-3.0-or-later. See [LICENSE](LICENSE).

Greenshot is a feature reference for this project and nothing more. ScrubShot is written from scratch and shares no code with it.
