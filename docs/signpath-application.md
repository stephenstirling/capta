# SignPath Foundation application: draft answers

A draft for the form at <https://signpath.org/apply> (free code signing for open-source projects). The form's
questions change, so treat this as source text to copy from. Everything below is true of the repository today;
anything in **[square brackets]** is something only the maintainer can fill in or confirm. Check SignPath's current
[terms and requirements](https://signpath.org/terms) before sending, and don't claim anything this file doesn't back up.

## Project

- **Name:** Capta
- **What it is:** a screen-capture app for Windows 11 (22H2 and later): region, window, full-screen, freeform and
  scrolling screenshots, screen recording, HDR-correct tone-mapping, on-device text recognition. It lives in the
  notification area and takes over the Print Screen key.
- **Repository:** <https://github.com/stephenstirling/capta> (public)
- **Licence:** Apache-2.0 ([LICENSE](../LICENSE))
- **Homepage:** <https://github.com/stephenstirling/capta>
- **Releases:** <https://github.com/stephenstirling/capta/releases>
- **Version history:** 1.0.0 (first release), 1.1.0 in preparation; see [CHANGELOG.md](../CHANGELOG.md).
- **Maintained:** yes. **[Add recent commit activity or anything else that shows it's active.]**
- **Platform and language:** C# on .NET 10, WinUI 3 (Windows App SDK), packaged as MSIX.

## Why the project needs signing

Capta is a packaged (MSIX) app. Windows refuses to install an MSIX package that isn't signed with a certificate it
trusts, so a download from GitHub can't be installed without one. The Microsoft Store signs the Store version, but
that's a separate package; people who want the open-source release from GitHub (or `winget` once it's listed) need a
signed package too. A paid certificate isn't an option for a free, single-maintainer project.

## What would be signed

One file per release: the `.msixbundle` (x64 and ARM64), built by the public
[release workflow](../.github/workflows/release.yml) from a `v*` tag. The package declares the capabilities it
needs in its manifest (`runFullTrust`, programmatic and borderless graphics capture, and the microphone only when the
user turns it on while recording). It contains no installer logic and no scripts that run outside the app.

## Build and release process

- Every pull request is built and tested by GitHub Actions ([build.yml](../.github/workflows/build.yml)).
- A version tag starts the release workflow: it runs the unit tests, builds the Release bundle on a GitHub-hosted
  runner and uploads it as an artifact. Nothing is built or signed on a developer's PC.
- A `sign` job submits that artifact to SignPath with `signpath/github-action-submit-signing-request`, so SignPath
  can verify the package came from this repository and workflow. The signing policy requires **manual approval**.
- The signed bundle is attached to a draft release, which the maintainer reviews and publishes.
- Details, including the one-time setup: [docs/releasing.md](releasing.md).

## Code signing policy and team

The README's *Code signing policy* section is published, with the attribution line and the roles:

| Role | Who |
| --- | --- |
| Authors and committers | Stephen Stirling (<https://github.com/stephenstirling>) |
| Reviewers | Stephen Stirling |
| Approvers | Stephen Stirling approves every signing request by hand |

The project has one maintainer, so all three roles are held by one person. **[Confirm two-factor authentication is
on for the GitHub account and for SignPath, which SignPath requires of approvers.]**

## What the software does with the network and with data

- It has no accounts, analytics, telemetry, advertising or crash reporting. Captures stay on the PC.
- Text recognition runs on the device through Windows' built-in OCR.
- The only network request is an optional update check in the GitHub build: one `GET` to the public GitHub Releases
  API, once a day, which can be switched off. It never downloads or installs anything; it opens the release page in
  the browser. The Microsoft Store build makes no network connections.
- Full text: [docs/privacy.md](privacy.md).

## Third-party components

| Component | Licence | Used for |
| --- | --- | --- |
| Windows App SDK / WinUI 3 (`Microsoft.WindowsAppSDK`) | Microsoft Software License Terms | UI and windowing |
| Windows SDK Build Tools (`Microsoft.Windows.SDK.BuildTools`) | Microsoft Software License Terms | build-time only |
| `H.NotifyIcon.WinUI` | MIT | the notification-area icon |
| xUnit, `Microsoft.NET.Test.Sdk` | Apache-2.0 / MIT | tests only, not in the package |

Nothing is downloaded at run time. **[Check this table against the packages in `Directory.Packages.props` before sending.]**

## Anything SignPath may ask for

- **Contact:** **[maintainer email]**
- **Why open source, and who uses it:** **[a sentence or two in your own words; link the Store listing once it's live]**
- **Malware and unwanted software:** Capta installs no other software, changes no settings other than its own, never
  changes Windows' Print Screen setting (it explains how to, and the user does it), and has a keyboard hook only for
  its own shortcuts. The source is public, so that can be checked.
