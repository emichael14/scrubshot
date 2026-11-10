# ScrubShot build specification

This is the product specification the project is built to. It states what ScrubShot does, the constraints it must honor and the order in which it is built. Decisions that go beyond it are recorded in `docs/decisions.md`.

Revision v1.1 (2026-11-01): two-layer detection. A small local AI model becomes a core part of detection (section 4, "Two-layer detection"); names and addresses move out of the add-ons.

## 1. Overview and working rules

ScrubShot is a free, open-source Windows screenshot and snipping tool with the full feature set of Greenshot, a cleaner Windows 11 look, and one extra layer built for the AI era: it automatically detects and blurs sensitive information (SSNs, EINs, Microsoft tenant/app IDs, client secrets, API keys, names, and more) before an image or text leaves the PC. It also has an optional clipboard monitor that scrubs sensitive text when it is pasted into risky destinations such as public AI chat sites.

The audience is IT professionals and employees in regulated industries whose companies want them using AI without leaking company data. Code quality, structure, README and commit history matter.

### Non-negotiables

- **Everything runs locally.** No network calls except an optional, configurable update check against GitHub Releases. No telemetry, analytics, crash reporting or cloud APIs of any kind.
- **Unscrubbed images never touch disk.** The original capture lives only in memory for the life of an editor session, and is cleared when the session ends.
- **Logs never contain captured content.** Debug logs record events and timings, never text, images, file names of captures or detected values.
- **Everything is configurable, and every setting can be enforced by an admin policy** (see section 5).
- **Original code only.** Greenshot is GPL and is a feature reference, not a source to copy from.
- **Safe by default.** Default sensitivity is Strict; protective settings default on.

### How to work

- Read this whole spec first, then propose a solution structure and the plan for Milestone 1 before writing code.
- Build milestone by milestone (section 7). Each milestone ends with a working, runnable app, passing tests and an updated README.
- Keep the detection engine free of UI dependencies so it can be unit tested on its own.
- When a decision isn't covered here, choose the more private and more configurable option, and record it in `docs/decisions.md`.
- Ask before adding any third-party dependency that phones home, needs a cloud key, or has a non-GPL-compatible license.

## 2. Tech stack and architecture

### Stack

- **Language/runtime:** C# on current .NET LTS.
- **UI:** WinUI 3 (Windows App SDK) with Mica backdrop. Use Win32 interop where WinUI lacks something (global hotkeys, full-screen capture overlay, clipboard listener).
- **Capture:** Windows.Graphics.Capture or GDI/DXGI, per-monitor DPI aware (PerMonitorV2).
- **OCR:** Windows.Media.Ocr (built in, local, returns word bounding boxes).
- **Detection, layer 1 (rules):** .NET regex plus custom validators (Luhn checks, SSN rules, entropy scoring) and context-label matching.
- **Detection, layer 2 (model, core):** a small local named-entity / PII recognition model run through ONNX Runtime on CPU over the same OCR text, shipped with the app and on by default. Requirements and selection process in section 4, "Two-layer detection".
- **Optional local ML (add-ons):** ONNX Runtime for face detection. QR/barcode detection via a local library such as ZXing.Net.
- **PDF add-on:** a local PDF library with a GPL-compatible license.
- **Tests:** xUnit for the engine; a corpus of sample images and text fixtures with expected detections.
- **Packaging:** MSIX plus a Win32 installer (or .intunewin-ready silent install) and a portable build.

### Solution layout

- `ScrubShot.App` – WinUI 3 app: tray, capture overlay, post-capture menu, editor, settings, toasts.
- `ScrubShot.Core` – shared models, settings, policy resolution, logging abstractions.
- `ScrubShot.Detection` – OCR wrapper, detector pipeline, all layer-1 detectors, the layer-2 model runner (ONNX Runtime), hit merging, sensitivity profiles. No UI references.
- `ScrubShot.Clipboard` – clipboard monitor, paste-destination detection, text scrubbing.
- `ScrubShot.AddIns` – add-on contract plus add-ons (faces, QR/barcodes, file and PDF scrubbing, metadata stripping).
- `ScrubShot.Tests` – unit and fixture tests.
- `deploy/` – installer, Intune package, ADMX/ADML policy templates, sample config.
- `docs/` – decisions log, detection catalog, admin guide.

### Pipeline

Capture → frozen screenshot of all monitors → region/window/full selection → crop in memory → OCR (words + boxes) → layer 1 (rule detectors) and layer 2 (local model) run on the same text, visual detectors on the image → merge overlapping hits with confidence scores → apply sensitivity profile → produce scrub regions → render scrubbed image → post-capture action (copy, editor, save, print).

Detectors share one interface (for example `IDetector` returning hits with type, confidence, bounding boxes and a masked preview) so new detectors and add-ons plug in without touching the pipeline. Keep extension points (policy provider, audit sink) behind interfaces defined in Core so alternative implementations can be added later as separate packages without rewriting the core.

## 3. Feature requirements

### 3.1 Tray and hotkeys

- Runs in the system tray; tray menu offers capture modes, open last capture, settings, exit.
- Optional launch at startup.
- Greenshot-style default hotkeys, all remappable: Print Screen = region, Alt+Print Screen = window, Ctrl+Print Screen = full screen, Shift+Print Screen = repeat last region.
- Detect and warn about hotkey conflicts (e.g. Windows Snipping Tool owning Print Screen) with a one-click fix suggestion.

### 3.2 Capture

- Region, window, full screen, specific monitor, repeat last region.
- On hotkey, grab all monitors first, then show a dimmed frozen overlay so menus and tooltips stay visible while selecting.
- Magnifier and crosshair coordinates while selecting (toggle).
- Optional mouse cursor capture and capture delay timer.
- Correct on mixed-DPI multi-monitor setups (e.g. 150% laptop next to 100% monitor): no blur, no offset.

### 3.3 Post-capture menu

- After release, a small menu appears at the cursor: Copy, Open in Editor, Save, Print, each with a single-key shortcut (C, E, S, P) plus Esc to cancel.
- Fully customizable: reorder, hide actions, change shortcut keys, or skip the menu and always run a chosen default.
- Quick Copy still auto-scrubs, then shows a toast such as "2 items scrubbed" with Undo and Open in Editor.

### 3.4 Editor

- Layout: slim tool strip on the left, contextual options bar on top (color, thickness, font, blur strength), Copy (primary) and Save at top right, scrubbed-items side panel on the right.
- Tools: select/move, crop, resize, rotate, rectangle, ellipse, line, arrow, freehand, text box, speech bubble, step counter, highlighter (text, area, grayscale-outside, magnify), manual blur and pixelate.
- Effects: drop shadow, border, torn edge.
- Every annotation stays editable after placing; full undo/redo.

### 3.5 Auto-scrub and side panel

- Auto-scrub runs on every capture when enabled; scrubbed spots get a soft aqua outline and a small type label.
- Side panel lists each item: type (e.g. "Email", "Tenant ID"), masked preview (e.g. `j***@kcc.com`), and a keep/reveal toggle. Hovering an item highlights its spot.
- Drag a box to scrub something missed; "Scrub everything like this" adds a rule from an item.
- Redaction styles: blur, pixelate, solid block, labeled block (e.g. \[SSN\]).
- Optional review screen (toggle): show the scrub result before copying.
- Original image exists only inside the editor session; Copy and Save output only the scrubbed result.

### 3.6 Output

- Copy to clipboard, save (PNG/JPG, configurable folder and file naming pattern), print, open with another program.
- Setting (default on): exclude ScrubShot clipboard writes from Windows clipboard history and cloud sync.

### 3.7 Clipboard monitor (optional, own settings section)

- Watches clipboard text; scrubs based on the paste destination, not at copy time.
- On Ctrl+V (and paste via menu where detectable), checks the foreground app and, for browsers, the active site; if the destination is on the risky list, it swaps in a scrubbed version just in time, then restores the original clipboard afterward.
- Destination lists: risky (default examples: browsers on public AI sites such as ChatGPT, Grok, Gemini; Teams; email) and trusted (password managers, admin portals). Admins can lock these, e.g. block every AI except the company-approved one.
- Per-detection-type on/off switches independent of screenshot settings.
- Behavior options: scrub silently, scrub with notice, or ask each time.

### 3.8 Settings window

Tabs: **General** (startup, save location, naming, format, updates, theme), **Hotkeys**, **Capture** (cursor, delay, magnifier, post-capture menu), **Scrubbing** (sensitivity level, detection checklist, AI detection layer on/off, redaction style, review screen, custom patterns, GUID handling), **Clipboard** (monitor on/off, destination lists, per-type switches, notice style), **Add-ons**, **Advanced** (debug logging, export/import settings). Settings locked by policy show a lock icon and are read-only.

### 3.9 Add-ons

- Lean core install; optional add-ons downloaded and enabled from the Add-ons tab (and pre-installable by admins): face detection, QR/barcode detection, file scrubbing (drag in images and PDFs), image metadata stripping (EXIF/GPS). Name and address detection is no longer an add-on; it is part of the core layer-2 model (section 4).
- Add-ons are verified (signature or hash) and run fully locally.

## 4. Detection catalog and sensitivity

### Detection categories

| Group | Types | Method |
| --- | --- | --- |
| Personal | SSN, phone, email, date of birth, driver's license-like numbers | Regex + validators (SSN area/group rules) |
| Financial | EIN, credit/debit card, bank routing and account-like numbers, IBAN | Regex + Luhn / checksum validation |
| Microsoft | Tenant ID, application (client) ID, object ID, subscription ID, Entra client secrets, connection strings (AccountKey=, Password=, SharedAccessKey=), storage keys, UPNs | GUID regex + nearby label matching; secret format patterns; key=value parsing |
| Other platforms | AWS access keys and secrets, GitHub tokens, Google API keys, Slack tokens, Stripe keys, JWTs, private keys (PEM blocks), generic API keys | Known prefixes/formats + entropy scoring |
| Credentials | Passwords next to "password"/"pwd" labels, high-entropy strings next to "secret", "key", "token", "value" | Context labels + entropy |
| Layer 2 (core model) | Person names, street addresses, secrets or credentials with no label and no known format, anything else the rules miss | Local NER / PII model via ONNX Runtime on the OCR text |
| Add-on: visual | Faces, QR codes, barcodes | Local image detectors |
| Custom | User or admin regex patterns with a label (e.g. player account numbers) | Regex, optional nearby-label requirement |

### Two-layer detection

Detection runs in two layers over the same OCR text, and both feed one merge step.

- **Layer 1, rules:** the regex, validator, entropy and context-label detectors above. Deterministic, fast, explainable.
- **Layer 2, model:** a small local named-entity / PII recognition model run through ONNX Runtime. It exists to catch what rules cannot: person names, street addresses, and secrets or credentials with no label and no recognizable format.
- **Merge:** hits from both layers carry a type and a confidence score. Overlapping hits merge into one scrub region keeping the highest-confidence type; sensitivity profiles decide what is scrubbed by default (Strict scrubs model hits at a low threshold, Balanced at a higher one, Relaxed only when a rule agrees, Custom per type).

Model requirements:

- Fully offline, CPU only, no GPU required. Runs on modest hardware (a 4-core laptop with 8 GB RAM).
- Adds no more than about 200 ms to a typical snip (one screen of text) on that hardware.
- Quantized, under roughly 100 MB on disk. Ships inside the app (installer, portable build and MSIX); no download at first run.
- License compatible with GPL-3.0-only; Apache 2.0 or MIT preferred. No usage restrictions, no attribution gates that break silent deployment, no phoning home.
- On by default, with a user setting and an admin policy to turn it off (`Scrubbing.AiLayerEnabled`). When off, layer 1 still runs.
- Ideally supports custom entity types (for example "API key", "client secret", "player account number") without retraining, so admins can extend it alongside custom regex patterns.
- Runs off the UI thread, is loaded once per process, and never receives anything but the OCR text of the current capture.

Selection process, before Milestone 3 starts: shortlist 2 or 3 candidate models; benchmark each against the detection fixture corpus for precision, recall, latency on the reference hardware and on-disk size, alongside a rules-only baseline; present the results to the owner and pick one with their decision. Record the choice in `docs/decisions.md` and the candidates' licenses in `docs/detection-catalog.md`.

### Context labels

GUIDs and random strings look identical regardless of meaning, so use OCR word positions: a hit is labeled and scored higher when a label such as "Tenant ID", "Directory ID", "Client ID", "Application ID", "Object ID", "Secret", "Value", "Key" or "Token" appears on the same line or directly above it.

### Sensitivity levels

- **Strict (default):** scrub every GUID, every high-entropy string and all enabled types.
- **Balanced:** scrub GUIDs only when a nearby label is found; obvious secrets and all personal and financial types.
- **Relaxed:** clear-cut items only (SSN, card numbers, EIN, PEM keys, known token formats).
- **Custom:** user ticks exactly which types blur by default and sets GUID handling.

### OCR robustness

Account for common OCR confusions (O/0, l/1/I, S/5, B/8) and spacing breaks by normalizing candidates before matching, and by merging adjacent word boxes when a pattern spans them. Prefer over-scrubbing to missing.

### Testing

Build a fixture corpus: screenshots of Entra app registrations, Azure portal blades, PowerShell output, emails, forms with fake SSNs/EINs, and clean images that must produce zero hits. Track precision and recall per type and fail CI on regressions.

## 5. Security, privacy and administration

### Data handling

- Unscrubbed captures live only in memory; buffers are cleared when the editor closes or the action completes. No temp files, no unscrubbed history.
- Clipboard originals held for just-in-time paste swapping stay in memory only and are released promptly.
- No network access except the optional update check. Document every outbound call in the README so security teams can verify it (e.g. block ScrubShot at the firewall and it still works).

### Admin policy (free, built in)

- Every setting can be set and locked through Windows policy: ship ADMX/ADML templates and read from `HKLM\Software\Policies\ScrubShot` (machine) and `HKCU\Software\Policies\ScrubShot` (user), so Intune, Group Policy or any MDM can manage it.
- Precedence: machine policy > user policy > user settings > defaults.
- Lockable examples: sensitivity level, enabled detection types, AI detection layer on or off, custom patterns, review screen forced on, auto-scrub forced on, clipboard monitor and destination lists, clipboard-history exclusion, update behavior, add-ons allowed or pre-installed.

### Updates

- Modes: automatic, notify only, or off. Checks GitHub Releases only.
- Admins can force "off" and deploy versions through Intune.
- Release builds are code-signed.

### Logging

- No telemetry or usage tracking.
- Opt-in debug logging (Advanced tab), written locally, with an "Open log folder" button for users to share logs in GitHub issues.
- Logs record events, timings, versions and errors only, never captured text, images, detected values or clipboard content. Add a test that fails if detected values appear in log output.

### Deployment

- Single installer with silent install switches, an Intune Win32 package recipe, and a portable build.
- Settings stored per user under AppData; policy from the registry paths above.
- Free and open source on GitHub under GPL-3.0-only, with a contributor license agreement (CLA Assistant).

## 6. Design system and UX

- **Feel:** simple and calm, Apple-like polish within Windows 11 Fluent design. Full power available, never cluttered.
- **Surfaces:** Mica/acrylic translucent backgrounds, rounded corners, generous spacing.
- **Theme:** follows system light/dark mode, with manual override.
- **Type:** Segoe UI Variable. **Icons:** thin line icons (Fluent System Icons).
- **Brand color:** aqua/teal accent for scrub outlines, labels and the primary Copy button.
- **App icon:** bubbles (scrub/soap theme), e.g. a camera or frame shape made of bubbles.
- **Delight moment:** a quick, subtle bubble shimmer over each spot as it gets scrubbed; respect the Windows reduce-motion setting.
- **Speed:** hotkey to overlay must feel instant; capture-to-clipboard in about a second including OCR on typical screens. OCR runs off the UI thread.
- **Keyboard first:** every common action reachable without the mouse.
- **Accessibility:** screen-reader labels, visible focus, sufficient contrast in both themes.

## 7. Milestones, repo setup and open questions

### Milestones

Each milestone is demo-able on its own.

1. **Foundation and capture.** Solution scaffold, tray app, hotkeys with conflict detection, frozen overlay, region/window/full/monitor/repeat capture, mixed-DPI correctness, copy and save. *Done when:* captures are pixel-accurate on a mixed-DPI two-monitor setup and Print Screen goes straight to region select.
2. **Post-capture menu and editor.** Customizable menu, full editor toolset, effects, undo/redo, print. *Done when:* every Greenshot core editor feature in section 3.4 works and annotations stay editable.
3. **Auto-scrub engine.** OCR, all layer-1 detectors, context labels, the layer-2 local model (chosen by the pre-milestone benchmark) and the merge step, sensitivity levels, side panel, redaction styles, review screen toggle, toast with Undo, bubble shimmer. *Done when:* the fixture corpus passes target recall with both layers on, a fake Entra app registration screenshot comes out with tenant ID, client ID and secret scrubbed, and a fake HR form comes out with the person's name and street address scrubbed. *Before it starts:* the model benchmark and owner decision described in section 4.
4. **Clipboard monitor.** Paste-destination detection, risky/trusted lists, per-type switches, notice options, clipboard-history exclusion. *Done when:* pasting a fake SSN into ChatGPT in a browser yields the scrubbed text while pasting into a trusted app yields the original.
5. **Settings, policy and add-ons.** All settings tabs, ADMX templates and registry policy with locks, add-on framework plus faces, QR/barcodes, file/PDF scrubbing and metadata stripping. *Done when:* an Intune-style registry policy locks sensitivity to Strict and the UI shows it locked.
6. **Packaging and launch.** Installer, silent install, Intune package recipe, portable build, code signing, update modes, opt-in debug logs, README, admin guide, contributing guide and CLA. *Done when:* a clean VM installs silently, runs offline, and updates (or doesn't) per policy.

### Repo setup

- Public GitHub repo `scrubshot`, GPL-3.0 `LICENSE`, `README.md`, `CONTRIBUTING.md`, CLA Assistant, issue templates (bug report asks for optional debug log).
- GitHub Actions: build, tests (including the detection corpus and the no-sensitive-data-in-logs test), release artifacts.
- `docs/decisions.md` for every decision made during the build; `docs/detection-catalog.md` kept in sync with the detectors.

### GitHub and Git workflow

- **Repo:** `https://github.com/emichael14/scrubshot`, public, GPL-3.0-only.
- **Branching:** `main` is protected by convention: never commit or push directly to it. Create one branch per milestone (`milestone-1-capture`, `milestone-2-editor`, ...) and smaller feature branches off it as needed.
- **Commits:** small, focused, descriptive messages (e.g. `feat(capture): mixed-DPI region overlay`). Commit and push to the working branch freely.
- **Pull requests:** at the end of each milestone, open a PR into `main` with `gh pr create`, summarizing what was built, how to test it and the acceptance criteria met. The owner reviews and merges; don't merge PRs yourself.
- **Never commit:** real secrets, keys, certificates or signing material, real sensitive data in test fixtures (use obviously fake values), build output (`bin/`, `obj/`), or user settings. Add a proper .NET/Visual Studio `.gitignore` first.
- **Releases:** create tags and GitHub Releases only when the owner asks.

### Open questions (don't block on these)

- Exact default lists of risky AI sites and trusted apps.
- Which layer-2 model to ship: decided by the benchmark and owner choice before Milestone 3 (section 4, "Two-layer detection").
