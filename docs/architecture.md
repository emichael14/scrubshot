# Architecture

Target architecture for the first six milestones. Projects marked *planned* do not exist yet; each is added in the milestone that needs it so the solution never carries empty shells. The reasoning behind these choices is in [decisions.md](decisions.md).

## Principles

- **Local only.** No component opens a network connection except the update checker, which lives in one class in `ScrubShot.App` and is the single documented outbound call.
- **One unscrubbed copy, in memory, owned by the session.** The frozen capture is created by the capture session, passed by reference through the pipeline, and disposed when the session ends or is cancelled. Nothing writes it to disk.
- **Detection has no UI dependencies.** `ScrubShot.Detection` references Core and the Windows OCR API only, so it is unit tested against text and image fixtures without a window.
- **Every setting resolves through policy.** Settings are read through a resolver that applies machine policy, user policy, user settings and defaults in that order and reports which one won, so the UI can show a lock.
- **The logging API cannot accept captured content.** Call sites pass event ids and numeric, boolean or timing fields only.
- **Seams for later private extensions.** Central policy service, audit logging and similar paid features plug into interfaces defined in Core; the open-source build ships the default local implementations.

## Solution layout

| Project | Purpose | Status |
| --- | --- | --- |
| `src/ScrubShot.Core` | Shared models, settings schema and JSON store, policy resolution, file naming patterns, capture geometry, logging abstractions. No Windows UI references. | Milestone 1 |
| `src/ScrubShot.Capture` | Monitor enumeration and DPI handling, `IScreenCapturer` with the GDI implementation, `FrozenScreen`, the Win32 selection overlay, window picker, cursor compositing. Win32 only, no WinUI. | Milestone 1 |
| `src/ScrubShot.App` | WinUI 3 application: tray icon and menu, hotkey service, capture coordinator, output (clipboard, file, print), post-capture menu, editor, settings window, notifications, update checker. | Milestone 1 onward |
| `src/ScrubShot.Detection` | OCR wrapper over Windows.Media.Ocr, detector pipeline, all detectors, context-label matching, sensitivity profiles, hit merging. | *planned, Milestone 3* |
| `src/ScrubShot.Clipboard` | Clipboard monitor, paste-destination detection (foreground app, browser site), just-in-time text scrubbing and restore. | *planned, Milestone 4* |
| `src/ScrubShot.AddIns` | Add-on contract, verification (signature or hash), and the add-ons: names and addresses (local NER), faces, QR and barcodes, file and PDF scrubbing, metadata stripping. | *planned, Milestone 5* |
| `tests/ScrubShot.Tests` | xUnit tests for Core, Capture geometry and Detection, plus the fixture corpus with expected detections and the no-sensitive-data-in-logs test. | Milestone 1 onward |
| `tools/CaptureCheck` | Console tool that captures all monitors to PNG and prints the monitor table, for pixel-accuracy checks on real hardware. | Milestone 1 |
| `deploy/` | Installer, Intune Win32 package recipe, ADMX/ADML policy templates, sample config. | *planned, Milestone 6* |
| `docs/` | Decisions log, architecture, milestone plans, detection catalog, admin guide. | ongoing |

Dependency direction: App → Capture, Detection, Clipboard, AddIns → Core. Nothing references App.

## Capture pipeline

```
hotkey or tray
  → capture all monitors into one FrozenScreen (physical pixels, per-monitor rects and DPI)
  → selection overlay (region, window, full screen, monitor, repeat last)
  → crop in memory
  → OCR: words with bounding boxes                       (Milestone 3)
  → detectors run on text and image, in parallel         (Milestone 3)
  → merge overlapping hits, apply sensitivity profile    (Milestone 3)
  → scrub regions → render scrubbed image               (Milestone 3)
  → post-capture action: Copy, Open in Editor, Save, Print
```

Stage notes:

- **Capture** runs first and immediately so the overlay shows exactly what was on screen when the hotkey fired, including open menus and tooltips. The process is PerMonitorV2 DPI aware; every coordinate in the pipeline is a physical pixel.
- **Overlay** is native Win32, one topmost window per monitor, each drawing its slice of the frozen image 1:1. A shared virtual-screen selection model lets a drag span monitors. The overlay is the only UI on the hot path and contains no XAML.
- **OCR and detection** run off the UI thread. OCR produces words with boxes; detectors receive both the text (with word positions, so labels on the same line or directly above can be matched) and the image (for visual add-ons such as faces and QR codes).
- **Merging** collapses overlapping hits, keeps the highest-confidence type, and applies the active sensitivity level (Strict, Balanced, Relaxed, Custom) to decide what is scrubbed by default.
- **Rendering** produces a new bitmap. The original stays alive only while the editor session is open so the user can toggle individual items; it is disposed when the session ends.
- **Output** receives only the scrubbed bitmap. Clipboard writes exclude themselves from Windows clipboard history and cloud sync by default.

## Detector contract

All detectors, built in or add-on, implement one interface in `ScrubShot.Detection`:

- Input: a `DetectionContext` with the OCR result (words, boxes, lines), the cropped image, the active sensitivity profile and the user's custom patterns.
- Output: a list of hits, each with a type (for example `Email`, `TenantId`, `AwsAccessKey`), a confidence, one or more bounding boxes in image coordinates, and a masked preview for the side panel (for example `j***@kcc.com`).
- Candidates are normalized before matching to absorb common OCR confusions (O/0, l/1/I, S/5, B/8) and spacing breaks, and adjacent word boxes are merged when a pattern spans them.
- Context labels nearby ("Tenant ID", "Client ID", "Secret", "Key", "Token") raise confidence and set the type; this is what tells one GUID from another.

Add-ons use the same interface and are loaded from verified packages. The `docs/detection-catalog.md` file (Milestone 3) lists every type, its method and its sensitivity-level behavior, and is kept in sync with the code.

## Settings and policy

- User settings: versioned JSON at `%LOCALAPPDATA%\ScrubShot\settings.json`, written atomically.
- Machine policy: `HKLM\Software\Policies\ScrubShot`. User policy: `HKCU\Software\Policies\ScrubShot`. Value names mirror setting paths, for example `Scrubbing.SensitivityLevel` or `Hotkeys.Region`.
- Resolution: machine policy > user policy > user settings > defaults. The resolver returns the value and its source so the UI can render locked settings read-only with a lock icon.
- ADMX/ADML templates (Milestone 5) are generated from the same settings schema so the two cannot drift.

## Deployment model

- Primary: unpackaged, self-contained (.NET and Windows App SDK both bundled), delivered as an installer with silent switches and as a portable zip. Works offline with no prerequisites.
- Secondary: MSIX built from the same project, for Store and MSIX-based Intune deployments.
- Updates: an optional check against GitHub Releases with modes automatic, notify only, or off; policy can force off.
