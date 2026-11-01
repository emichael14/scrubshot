# Security policy

## Reporting a vulnerability

Please do not open a public issue for security problems. Use GitHub's private vulnerability reporting for this repository:

1. Open the repository's **Security** tab and choose **Report a vulnerability**, or go directly to <https://github.com/emichael14/scrubshot/security/advisories/new>.
2. Describe the problem, the ScrubShot version, your Windows build, steps to reproduce and the impact you see.
3. Never include real sensitive data (secrets, identifiers, personal or financial information) in a report. Use obviously fake values.

Reports are handled through GitHub's advisory workflow, so the discussion stays private until a fix is available.

## What to report here

- Unscrubbed capture content reaching disk, logs or the network
- A systematic way to bypass detection or scrubbing (a single OCR miss on one image is a detection gap, not a vulnerability; file those with the Detection gap issue template)
- A user being able to override a setting that policy has locked
- The clipboard monitor delivering original content to a destination on the risky list
- ScrubShot contacting any network endpoint other than the documented update check
- Add-on verification bypass, or an add-on running code that was not verified
- Anything in the installer, updater or portable build that could be used to run untrusted code

## Supported versions

ScrubShot is pre-alpha and has no releases yet. Once releases begin, security fixes target the latest release only.

## Response

This is an open-source project maintained on a best-effort basis. Reports are acknowledged as quickly as possible, fixes ship as a new release, and a GitHub Security Advisory is published once the fix is available.
