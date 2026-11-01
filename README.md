# ScrubShot

<img src="assets/icon/scrubshot-128.png" width="96" height="96" alt="ScrubShot icon: three glossy aqua soap bubbles" align="right">

<!--
  Demo GIF placeholder. Target: docs/media/demo.gif, about 10 seconds, under 5 MB.
  Show: Print Screen -> drag a region -> aqua scrub outlines appear -> Copy -> paste shows the blurred result.
  Replace this comment with: ![ScrubShot demo](docs/media/demo.gif)
-->

**A free, open-source screenshot tool for Windows that scrubs sensitive data before it leaves your PC.**

ScrubShot gives you the full Greenshot-style capture and annotation workflow with a clean Windows 11 look, plus one layer built for the AI era: every capture is OCR'd locally, and anything sensitive (SSNs, EINs, card numbers, Microsoft tenant and client IDs, client secrets, API keys, connection strings, emails and more) is detected and blurred automatically before the image reaches the clipboard or disk. An optional clipboard monitor does the same for text the moment you paste into a risky destination such as a public AI chat site.

For IT professionals and employees in regulated industries whose organizations want them to use AI tools without leaking company data.

> **Status: pre-alpha.** Milestone 1 (foundation and capture) is in progress. Nothing is ready to install yet. See the [roadmap](#roadmap).

## Privacy commitments

ScrubShot is built to the commitments below. Until the milestone that implements each one ships, read them as design intent rather than verified behavior. Each will be covered by tests and documented here as it lands.

- **All processing happens locally.** No telemetry, analytics, crash reporting or cloud APIs. The only network call ScrubShot will make is an optional, configurable update check against GitHub Releases, which admins can turn off. ScrubShot is designed to keep working when blocked at the firewall.
- **ScrubShot never writes an unscrubbed image to disk while auto-scrub is on.** The original capture is held in memory for the life of an editor session and cleared when the session ends. Copy and Save output only the scrubbed result.
- **Logs never contain captured content.** Opt-in debug logs record events, timings, versions and errors, never text, images, file names of captures or detected values. A test is planned that fails the build if that changes.

## Limitations

- **Detection is a safety net, not a guarantee.** It recognizes known patterns and formats. A value outside those patterns, or one whose shape the detectors have never seen, can pass through unscrubbed.
- **OCR can miss or misread text.** Small fonts, low contrast, dense layouts and stylized text reduce accuracy, and a value that is read incorrectly may not match a detector. ScrubShot normalizes common OCR confusions and prefers over-scrubbing, but it cannot catch what it cannot read.
- **Review before sharing.** The scrubbed-items side panel and the optional review screen exist so you can see what was and was not caught. Check the result before it leaves your machine.
- **ScrubShot does not replace company DLP.** It reduces accidental leaks at the moment of capture and paste on one PC. It is not a data loss prevention platform, does not inspect other applications or network traffic, and is not an audit or compliance control.

## Built for IT admins

These are design commitments as well; the policy layer arrives with Milestone 5.

- **Safe by default.** Default sensitivity is Strict and protective settings default to on, so an unmanaged install is already conservative.
- **Every setting is designed to be admin-enforceable** through Windows policy at `HKLM\Software\Policies\ScrubShot` and `HKCU\Software\Policies\ScrubShot`, with ADMX/ADML templates, so Intune, Group Policy or any MDM can set and lock it. Precedence: machine policy, then user policy, then user settings, then defaults. Locked settings show a lock icon and are read-only in the UI.
- **Lockable examples.** Sensitivity level, enabled detection types, the AI detection layer, custom patterns, review screen forced on, auto-scrub forced on, clipboard monitor and its destination lists, clipboard-history exclusion, update behavior, which add-ons are allowed or pre-installed.
- **Deployment.** Planned: one installer with silent switches, an Intune Win32 package recipe, a portable build and an MSIX package. Updates can be automatic, notify-only or off; admins can force off and deploy versions themselves.
- **Verifiable network behavior.** Every outbound call will be listed in this README so security teams can confirm there is exactly one and block it if they choose.

## What it will do

**Capture.** Region, window, full screen, specific monitor and repeat-last-region, with Greenshot-style hotkeys (Print Screen, Alt, Ctrl and Shift variants, all remappable). A frozen, dimmed overlay keeps menus and tooltips visible while you select. Magnifier and crosshair coordinates. Pixel-accurate on mixed-DPI multi-monitor setups.

**Post-capture menu.** Copy, Open in Editor, Save, Print with single-key shortcuts. Reorder, hide, remap, or skip the menu and always run a default.

**Editor.** Crop, resize, rotate, shapes, arrows, freehand, text, speech bubbles, step counters, highlighters, manual blur and pixelate, effects (shadow, border, torn edge). Every annotation stays editable. Full undo and redo.

**Auto-scrub.** Local OCR plus two detection layers over the same text. Layer 1 is rules: patterns and validators for personal data, financial data, Microsoft identifiers and secrets, third-party API tokens and credentials, with context labels ("Tenant ID", "Secret", "Key") raising confidence. Layer 2 is a small local AI model (named-entity and PII recognition, run on the CPU through ONNX Runtime, shipped with the app, on by default, switchable by setting and policy) that catches what rules cannot: person names, street addresses, and secrets with no label or known format. Both layers merge into one set of scrub regions with confidence scores, and sensitivity levels Strict, Balanced, Relaxed and Custom decide what is scrubbed. A side panel lists each scrubbed item with a masked preview and a keep/reveal toggle. Redaction styles: blur, pixelate, solid block, labeled block.

**Clipboard monitor (optional).** Scrubs text based on where you paste, not when you copy. Risky and trusted destination lists, per-type switches, silent/notify/ask behaviors.

**Add-ons (optional, local).** Face detection, QR and barcode detection, file and PDF scrubbing, image metadata stripping.

## Architecture

The solution layout, project boundaries and the capture-to-output pipeline are described in [docs/architecture.md](docs/architecture.md).

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

- [ ] **1. Foundation and capture.** Tray app, hotkeys with conflict detection, frozen overlay, all capture modes, mixed-DPI correctness, copy and save, CLA Assistant setup. ([plan](docs/milestone-1-plan.md))
- [ ] **2. Post-capture menu and editor.** Customizable menu, full editor toolset, effects, undo/redo, print.
- [ ] **3. Auto-scrub engine.** OCR, rule detectors, context labels, the local AI detection layer (model chosen by benchmark first), sensitivity levels, side panel, redaction styles, review screen.
- [ ] **4. Clipboard monitor.** Paste-destination detection, risky/trusted lists, per-type switches.
- [ ] **5. Settings, policy and add-ons.** All settings tabs, ADMX templates, registry policy with locks, add-on framework (faces, QR and barcodes, files and PDFs, metadata).
- [ ] **6. Packaging and launch.** Installer, silent install, Intune recipe, portable build, code signing, update modes, admin guide, contributing guide.

## Project documents

- [Specification](SPEC.md): what ScrubShot does, its constraints and the build order.
- [Architecture](docs/architecture.md): projects, boundaries and the capture pipeline.
- [Decisions log](docs/decisions.md): every design decision not covered by the spec, with reasons.
- [Milestone 1 plan](docs/milestone-1-plan.md)

## Contributing

Bug reports, detection gaps and feature ideas are welcome now through the [issue templates](.github/ISSUE_TEMPLATE). Code contributions open once the contributor license agreement is in place (CLA Assistant, planned for Milestone 1); pull requests opened before then will be held until it is. Please never attach screenshots or logs containing real sensitive data; use obviously fake values.

## Security

Report vulnerabilities privately through GitHub's private vulnerability reporting rather than a public issue. See [SECURITY.md](SECURITY.md).

## License

GPL-3.0-only. See [LICENSE](LICENSE).

Greenshot is a feature reference for this project and nothing more. ScrubShot is written from scratch and shares no code with it.
