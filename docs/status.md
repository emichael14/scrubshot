# Project status

Updated at the end of every session. Newest entry first. Keep each entry short: where things stand, what is blocked, what comes next.

## 2026-11-01

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

**Open questions carried from the spec (do not block on them)**

- Ownership entity (Technest Valley LLC or a ScrubShot LLC) for the copyright line.
- Default risky AI site list and trusted app list for the clipboard monitor.
- Which NER model for names and addresses, given size and license.
