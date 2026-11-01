# ScrubShot: Claude Code project instructions

ScrubShot is a free, open-source Windows screenshot tool (C#, .NET 10, WinUI 3) that detects and blurs sensitive data locally before an image or text leaves the PC. The authoritative requirements are the build spec; its local path is in `CLAUDE.local.md`. This file covers how to work in this repo. The spec covers what to build.

## Start of every session

Work through this list first. If a check fails, decide by this rule:

- **Fix it yourself and report what you did** when the issue is routine, reversible dev tooling: package feeds (nuget.org only), project templates, SDK workloads, local build configuration.
- **Stop and ask the owner first** for anything that needs admin rights, changes system or security settings, installs software outside the dev toolchain, touches anything outside `C:\Dev`, or adds a package source other than nuget.org.

1. **Read state.** `docs/status.md` (where things stand and what is next), then the current milestone plan in `docs/`, then skim `docs/decisions.md`. Before starting a new milestone, read the whole spec.
2. **Git.** `git status`, `git branch --show-current`, `git fetch origin`. Never work on `main`. The working branch must not be behind its remote, and the tree should be clean or explained in `docs/status.md`.
3. **GitHub.** `gh auth status` must show the owner's account. `gh pr list --state all` shows whether the current milestone PR is open or merged. If `gh` is not found, see `CLAUDE.local.md`.
4. **Toolchain.** `dotnet --list-sdks` must list a 10.0.4xx SDK. If it does not, stop and ask the owner to run `winget install Microsoft.DotNet.SDK.10` in an elevated terminal. `dotnet new list winui` must list the WinUI templates; if not, run `dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates` (no elevation needed). Once a solution exists, `dotnet build` and `dotnet test` must be green before new work starts.
5. **Microsoft Learn.** Confirm the `microsoft-learn` MCP tools are available (ToolSearch for "microsoft learn"). Verify every Win32, Windows App SDK and WinUI API claim against Learn before relying on it. Never replace a working setup on an unverified assertion.
6. **Monitors.** Record the layout; capture work and the mixed-DPI acceptance test depend on it. Note in `docs/status.md` whether mixed-DPI testing is possible this session.
   ```powershell
   Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Screen]::AllScreens | Select-Object DeviceName, Primary, Bounds
   ```
7. **Print Screen ownership** (when touching hotkeys). `HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled` set to 1 means Snipping Tool holds Print Screen.
8. **Docs in sync.** Every decision from the previous session has an entry in `docs/decisions.md`. `docs/status.md` matches reality. The README status line matches the milestone. From Milestone 3 on, `docs/detection-catalog.md` matches the detectors.

## End of every session

- Update `docs/status.md`: date, done, in flight, blockers, next step. Commit and push the working branch.
- Record every decision made in `docs/decisions.md`.
- Propose memory updates to the owner in the final message. Do not save memory silently.

## Non-negotiables (from the spec)

- **Local only.** No network calls except the optional, configurable update check against GitHub Releases. No telemetry, analytics, crash reporting or cloud APIs.
- **No unscrubbed image on disk.** The original capture lives in memory for the editor session and is cleared when it ends. No temp files, no unscrubbed history.
- **Logs never contain captured content.** Events, timings, versions and errors only. The logging API must not accept captured text, capture file names or detected values.
- **Everything configurable, everything policy-enforceable.** Precedence: machine policy > user policy > user settings > defaults. Locked settings show a lock and are read-only.
- **Safe by default.** Strict sensitivity; protective settings default on.
- **Original code only.** Greenshot is a feature reference, never a source.
- **Detection has no UI dependencies** so it can be unit tested alone.
- When the spec is silent, choose the more private and more configurable option and record it in `docs/decisions.md`.
- **Ask before adding any third-party dependency** that phones home, needs a cloud key, or has a license incompatible with GPL-3.0-only.

## Git and GitHub workflow

- `main` is protected by convention. Never commit to it, never push to it, never merge a PR. The owner reviews and merges.
- One branch per milestone (`milestone-1-capture`, `milestone-2-editor`, ...), feature branches off it as needed. Commit and push to the working branch freely.
- Commits: small, focused, conventional messages (`feat(capture): mixed-DPI region overlay`, `docs(readme): ...`, `chore(repo): ...`).
- At the end of a milestone, open a PR into `main` with `gh pr create` summarizing what was built, how to test it and which acceptance criteria are met.
- Never commit secrets, keys, certificates, signing material, real sensitive data in fixtures (use obviously fake values), build output, or user settings.
- Tags and releases only when the owner asks.
- Creating new public surfaces (repositories, releases) needs the owner; the permission classifier blocks it from autonomous sessions.

## Conventions

- .NET 10 LTS, `net10.0-windows10.0.26100.0`, minimum OS 10.0.19041. Windows App SDK 2.x. Unpackaged and self-contained is the primary deployment; MSIX is secondary.
- Layout: `src/` for projects, `tests/` for tests, `tools/` for dev tools, `deploy/` for packaging, `docs/` for documentation. Projects are added in the milestone that needs them, never as empty shells.
- Every source file starts with `// SPDX-License-Identifier: GPL-3.0-only` and the placeholder copyright line from decision D-015.
- P/Invoke through CsWin32 (`NativeMethods.txt`), no hand-written `DllImport`. MVVM through CommunityToolkit.Mvvm. Tests in xUnit.
- Every coordinate in the capture pipeline is a physical pixel; the process is PerMonitorV2.
- The README stays plain and impersonal: no author material, nothing about who built the project or how. Privacy and admin behaviors are described as commitments until they ship with tests.
- Decision entries are numbered `D-nnn` with Decision, Why and, where useful, Considered.

## Where things are

| Need | File |
| --- | --- |
| Current state and next step | `docs/status.md` |
| Milestone plans | `docs/milestone-N-*.md` |
| Decisions and rationale | `docs/decisions.md` |
| Architecture and pipeline | `docs/architecture.md` |
| Machine-specific notes, spec path, tooling quirks | `CLAUDE.local.md` (not committed) |
