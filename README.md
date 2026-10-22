# Capta

Fast, HDR-correct screen capture for Windows 11. Capta lives in the notification area,
takes over the Print Screen key and copies captures to the clipboard as PNG.

## Install

Capta needs Windows 11 22H2 or later.

- **Microsoft Store:** the easiest way, with automatic updates. *The Store listing isn't live yet.* This page will
  link to it, and `winget install` will find it, as soon as it is.
- **Build it yourself:** follow [Build](#build) below. It deploys the package for your own account and needs
  Visual Studio (or the .NET 10 SDK) and Windows' Developer Mode.

There's no installer to download from GitHub yet. Windows only installs packages that are signed with a certificate
it trusts, and Capta's GitHub releases will be signed through SignPath Foundation's free programme for open-source
projects (see [Code signing policy](#code-signing-policy)). Until that's in place, the Store and building from
source are the two options.

## Shortcuts

| Keys | Action |
| --- | --- |
| PrtSc | Region capture |
| Shift+PrtSc | Full screen, straight to the clipboard |
| Alt+PrtSc | Active window |
| Ctrl+PrtSc | Record video: choose an area, then Ctrl+PrtSc or Stop to finish |
| Ctrl+Shift+PrtSc | Grab text from screen: select a region and its text is copied |
| Ctrl+Alt+PrtSc | Repeat last region: captures the last Region selection again, without the overlay (also in the tray menu and on the toolbar) |

In the region overlay, letting go of the mouse captures the area you drew (and copies it, with Auto-copy on);
**Esc** cancels. Scrolling and Record keep the selection adjustable until you press Enter. Press **C** to pick the colour under the cursor: its hex code is copied and
pinned as a small chip (click the code to copy again, double-click or Esc to unpin). The settings button at
the end of the overlay's bar switches Auto-copy, Copy for chat and Redact personal info, and opens the settings windows.

**Redact personal info** (off by default; in Settings or the overlay's Capture settings) reads each capture's text
on the device and covers email addresses, phone numbers and long digit runs (13 or more digits, such as card
numbers) with a solid fill before the capture is copied, saved or sent to Ocula. The card that follows says what
was covered, with **Review** (a switch for each item) and **Undo**. It only sees what OCR reads, so check anything
sensitive, and it will cover some numbers that aren't personal: that's what Review and Undo are for.

**Copy for chat** (the card's Copy menu, or a switch in the overlay's Capture settings that makes Auto-copy use
it) puts a smaller copy on the clipboard for AI chats and messaging apps: sRGB, no metadata, long edge at most
1568 px (never scaled up), as a PNG, or a JPEG (quality 85) if the PNG would be over 1 MB. The card shows the
size, for example "Copied for chat · 1568 × 980 · 412 KB".

Window captures (Window mode and Alt+PrtSc) keep the window's rounded corners, with a transparent background
outside them. The mouse cursor is left out of screenshots unless you switch on **Include cursor in screenshots**
(Settings, or the overlay's Capture settings); it then appears in window and full-screen captures. Region
captures never include it, since the cursor is the selection tool there.

**Updates.** The Microsoft Store updates the Store build. The website build can check for a newer release: once a
day in the background (switch it off in Settings), or on demand from the tray menu's **Check for updates**. It
reads the latest release from GitHub Releases and shows a card, "Version 1.1.x is available · What's new ·
Download", which opens the release page in your browser. Capta never downloads or installs an update itself,
sends nothing about you, and makes no other network calls.

**Colour & HDR** (tray menu → Settings…, then the Colour & HDR link) sets how HDR screens are tone-mapped, and whether saved files are sRGB or
Display P3 (which keeps wide-gamut colours and embeds its profile; the clipboard always gets sRGB). It can also
correct captures of an SDR monitor for its colour profile, so they look as they did on a calibrated or
wide-gamut screen.

Recordings are MP4 (H.264 + AAC) in your capture folder, with system audio and, if you switch it on,
the microphone. The card after a recording can play it, copy the file or save a GIF of the first
30 seconds. The floating toolbar is in the tray menu.

Win+PrtSc and Win+Shift+S stay with Windows. If Windows is set to open Snipping Tool on Print Screen,
Capta shows how to turn that off in Settings.

**Keyboards without a Print Screen key** (for example the Logitech MX Keys, whose camera key is handled by
Logi Options+): set that key's action in the keyboard's software to send **Print Screen**, not its own
"Screen capture". Capta accepts Print Screen sent by such software, so Shift, Alt and Ctrl+Alt with the key
work as in the table above. It ignores every other key sent by software, so scripts can't start a capture.

## Requirements

- Windows 11 22H2 (10.0.22621) or later
- Visual Studio 2026 with the **WinUI application development** workload, or the .NET 10 SDK
- Developer Mode enabled (to deploy the unsigned debug package)

## Build

```powershell
msbuild Capta.slnx -restore -p:Configuration=Debug -p:Platform=x64
```

Open `Capta.slnx` in Visual Studio and press F5 to deploy and run the packaged app.

Unit tests (colour maths, PNG and ICC handling, cropping, scroll stitching, shortcuts) need no screen:

```powershell
dotnet test tests\Capta.Tests\Capta.Tests.csproj -p:Platform=x64
```

GitHub Actions builds and tests every pull request (`.github/workflows/build.yml`).

## Layout

| Path | Purpose |
| --- | --- |
| `src/Capta` | The WinUI 3 app, packaged as MSIX (single-project packaging). |
| `src/Stirling.Shared` | Brand resources shared with other Stirling apps (e.g. Ocula): theme colours and app icons. |
| `tests/Capta.Tests` | xUnit tests for the app's pure logic; they compile its source files directly. |
| `tools/generate_icons.py` | Regenerates the logo/ico set in `src/Stirling.Shared/Assets/<App>`. |
| `tools/generate_display_p3_icc.py` | Regenerates the Display P3 profile saved PNGs embed. |

### Using Stirling.Shared from another app

1. Reference `src/Stirling.Shared/Stirling.Shared.csproj`.
2. Merge the theme in `App.xaml`, after `XamlControlsResources`:
   ```xml
   <ResourceDictionary Source="ms-appx:///Stirling.Shared/Themes/Colors.xaml" />
   ```
3. Link the icon set by adding to the app's `.csproj`:
   ```xml
   <PropertyGroup><StirlingIconSet>Ocula</StirlingIconSet></PropertyGroup>
   <Import Project="..\Stirling.Shared\build\Stirling.Shared.AppIcons.targets" />
   ```

## Docs

- [Privacy policy](docs/privacy.md)
- [Ocula integration](docs/ocula-integration.md): the `ocula:` URIs and the PNG metadata
- [Store submission](docs/store-submission.md): checklist, certification notes and the draft listing
- [Releasing](docs/releasing.md): cutting a release and code signing with SignPath Foundation

## Store identity

`Package.appxmanifest` uses a placeholder identity (`StephenStirling.Capta`, `CN=StephenStirling`).
Before submitting, run **Project → Publish → Associate App with the Store** in Visual Studio.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

Capta's Windows release packages on the [Releases](https://github.com/stephenstirling/capta/releases) page are signed
this way. *Status: SignPath Foundation hasn't approved the project yet, so until it has, GitHub releases carry no
installer and the Microsoft Store version (signed by Microsoft) is the way to install Capta.*

**Team roles**

| Role | Who |
| --- | --- |
| Authors and committers | [Stephen Stirling](https://github.com/stephenstirling) |
| Reviewers | Stephen Stirling. Every change is built and tested by GitHub Actions on its pull request. |
| Approvers | Stephen Stirling approves every signing request by hand. |

**How a release is signed.** A version tag (for example `v1.1.0`) starts the public
[release workflow](.github/workflows/release.yml), which tests the code, builds the package from that tag in GitHub
Actions and submits it to SignPath. SignPath checks that the package was built from this repository, and the signing
request waits for the approver. Nothing is signed from a developer's PC, and the signing key never leaves SignPath.
Details are in [docs/releasing.md](docs/releasing.md).

**Verifying a download.** In PowerShell, `Get-AuthenticodeSignature .\Capta_<version>_x64_arm64.msixbundle` should
report `Valid`, with `SignPath Foundation` as the signer.

**Privacy.** Capta does not collect, store or send personal information; captures stay on your PC. The only network
request is the optional daily update check in the GitHub download (one request to `api.github.com`), which you can
switch off, and the Microsoft Store version makes none. See the full [privacy policy](docs/privacy.md). Capta uses
no third-party services or components that send data elsewhere; text recognition runs on-device through Windows.

## Support

Capta is made by [Stephen Stirling](https://github.com/stephenstirling). If it's useful to you,
you can [buy me a coffee](https://buymeacoffee.com/stephenstirling).

## Licence

Apache-2.0. See [LICENSE](LICENSE).
