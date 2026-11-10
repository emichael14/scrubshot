# Milestone 1 manual test sitting

Milestone 1 is done when captures are pixel-accurate on a mixed-DPI two-monitor setup and Print Screen goes straight to region select. The automated tests cover the arithmetic and the rules; the checks that need real hardware, a real tray and real key presses are collected in the hardware table in [status.md](../status.md#hardware-tests-before-the-milestone-1-pr-is-merged). This page is the order to run them in one sitting, the setup, and the cleanup.

The table in `status.md` is the single list of tests, with the exact command and what to report for each. This page does not repeat them.

## What the automated tests already prove

Run `dotnet test` from the repository root. It covers, on synthetic data:

- Settings: precedence (machine policy, user policy, user file, defaults), locks, validation, and that a locked setting cannot be changed or written to the user's file.
- File naming, collision handling and save-folder resolution without the current directory.
- Capture geometry on synthetic layouts: a 150 percent laptop beside a 100 percent monitor, negative origins, stacked monitors.
- The selection model (drag across monitors, clamping, keyboard nudging) and the capture coordinator (every mode, cancel, busy, failures, the frozen screen disposed on every path).
- Output: PNG and JPEG encoding, the `CF_DIB` payload, the clipboard write order and its rollback, partial-file cleanup, per-output results and messages.
- Hotkey gesture parsing and the registration rules.
- That log methods cannot take text and that the shipped libraries reference no networking assembly.

## Setup

1. Sign in on the machine with the two monitors, one of them at 125 or 150 percent scale.
2. Quit Greenshot and any other screenshot tool; they hold the Print Screen combinations.
3. From the repository root:

   ```powershell
   git checkout milestone-1-capture
   git pull
   dotnet build ScrubShot.sln -c Release
   dotnet test -c Release --no-build
   mkdir C:\Dev\scrubshot-hw
   ```

4. Note whether `%LOCALAPPDATA%\ScrubShot\settings.json` exists, so you can put things back afterwards.

## Order

Group by what has to be running. The numbers are the rows in the `status.md` table.

| Stage | Rows | Notes |
| --- | --- | --- |
| A. Command-line capture checks, app not running | H1, H2, H5, H6, H7 | These write PNGs into `C:\Dev\scrubshot-hw`. They are the pixel-accuracy and overlay tests for the acceptance criterion. |
| B. Hotkeys from the command line, app not running | H8 | Includes turning the Windows "Print screen key" setting off by hand and checking that Print Screen then reaches ScrubShot. |
| C. The app | H3, H4, H9, H10, H11, H18, H20 | Tray icon, menu, second launch, startup entry, repeat last region after unplugging a monitor, process priority. |
| D. Output through the app | H12, H13, H14 | Clipboard, clipboard history, save, Open last capture. |
| E. Settings window | H15, H16, H17, H19 | Settings pages, the Snipping Tool fix button, a policy lock, the window on both monitors. H17 needs an elevated terminal for one command and its undo. |

If stage A fails on the mixed-DPI setup, stop and report that first; the later stages do not depend on DPI.

## What to send back

For each row: pass or fail, and the specific values the "Report back" column asks for (the printed monitor table, PNG sizes, the binding lines). For a failure, the PNGs from `C:\Dev\scrubshot-hw` are the most useful evidence. They are screenshots of your desktop, so check them before sharing and do not attach them to a public issue.

## Cleanup

1. Exit ScrubShot from the tray menu.
2. Remove the startup entry if you do not want the test build to start with Windows: turn "Start ScrubShot when you sign in" off in Settings before exiting, or delete the `ScrubShot` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
3. Delete `C:\Dev\scrubshot-hw` and any test captures in `Pictures\ScrubShot`.
4. Restore or delete `%LOCALAPPDATA%\ScrubShot\settings.json` as you found it.
5. If you used the Snipping Tool fix and want the Windows default back: Settings, Accessibility, Keyboard, "Use the Print screen key to open screen capture".
6. Undo the policy value from H17 with the command in that row.
