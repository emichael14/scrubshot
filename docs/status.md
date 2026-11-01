# Project status

Updated at the end of every session. Newest entry first. Keep each entry short: where things stand, what is blocked, what comes next.

## 2026-11-01 (session 2)

**Milestone:** 1 (foundation and capture). **Step 1 (scaffold) done** on branch `milestone-1-capture`. Owner approved the plan and its five assumptions in the session prompt; PR #1 is still open (see D-024 for the branch base).

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

**Blocked or waiting on the owner**

- Merge PR #1 so `main` carries the docs. `milestone-1-capture` already contains those commits (D-024).
- Right-click the tray icon once and confirm Exit ends the process cleanly (the only path not covered by the automated check).
**Next**

1. Milestone 1 step 2: Core settings and policy (settings records, versioned JSON store with atomic writes, policy resolver with source reporting, file name pattern engine) with unit tests. See `docs/milestone-1-plan.md`.
2. Step 3: capture engine (`MonitorLayout`, `IScreenCapturer`, `GdiScreenCapturer`, `FrozenScreen`) with synthetic-layout geometry tests.

**Environment notes**

- Single 1920x1080 display at 100% on 2026-11-01. Mixed-DPI testing was not possible this session.
- `HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled` is not set on this machine; Snipping Tool 11.2608 is installed.
- `dotnet test` is in Microsoft.Testing.Platform mode (D-022). VSTest flags such as `--logger trx` are rejected; use `--report-xunit-trx`.
- Windows 11 hides new tray icons in the overflow area by default; use "Other system tray icons" in Taskbar settings to pin ScrubShot while developing.

**Open questions carried from the spec (do not block on them)**

- Ownership entity (Technest Valley LLC or a ScrubShot LLC) for the copyright line.
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
