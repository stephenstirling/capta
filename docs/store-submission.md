# Microsoft Store submission

A checklist and draft listing for submitting Capta in Partner Center. The listing below describes only
features that ship today. Freeform, Scrolling, Record and Copy as HDR show as disabled in the
toolbar and aren't advertised.

## 1. Reserve the name and associate the app (you)

1. Partner Center → **Apps and games → New product → MSIX or PWA app** → reserve **Capta**. If it's
   taken, reserve an alternative such as "Capta Screen Capture". The display name in
   `Package.appxmanifest` must match it.
2. In Visual Studio: **Project → Publish → Associate App with the Store**, and pick the reservation.
   This replaces the placeholder identity (`StephenStirling.Capta`, `CN=StephenStirling`) in
   `Package.appxmanifest` with the real Name, Publisher and PublisherDisplayName. Commit that change.

## 2. Build the upload package

```powershell
msbuild src\Capta\Capta.csproj -restore -p:Configuration=Release -p:Platform=x64 `
  -p:GenerateAppxPackageOnBuild=true -p:UapAppxPackageBuildMode=StoreUpload `
  -p:AppxBundle=Always "-p:AppxBundlePlatforms=x64|arm64" -p:AppxPackageSigningEnabled=false
```

The `.msixupload` lands under `bin\x64\Release\…\AppPackages\` (or under `StirlingLocalBuildRoot` when
that's set). Release builds are self-contained and partially trimmed, about 34 MB per architecture.
The Store signs the package, so leave local signing off. Bump the `Version` in
`Package.appxmanifest` for every resubmission.

Before uploading, install the Release build and do a quick test: region, window and full-screen
capture, Grab text, the card's Copy, Save and Pin buttons, settings, and a restart to check Recent.

## 3. Properties

| Field | Value |
| --- | --- |
| Category | Productivity (subcategory: none), or Utilities & tools |
| Privacy policy URL | `https://github.com/stephenstirling/capta/blob/main/docs/privacy.md` |
| Website | `https://github.com/stephenstirling/capta` |
| Support contact | `https://github.com/stephenstirling/capta/issues` |
| System requirements | Windows 11 22H2 or later. HDR handling applies on HDR displays. |
| Product declarations | Does **not** access, collect or transmit personal information. Third-party purchase API: **yes** (see §5). |

## 4. Age rating

Complete the IARC questionnaire: no violence, no user-generated content shared online, no
purchases inside the app, no location, and no data sharing. Expected result: 3+ / Everyone.

## 5. Restricted capabilities and policy notes (Submission options → Notes for certification)

Paste this in **Notes for certification**, and use the capability lines where Partner Center asks
why each restricted capability is needed:

> Capta is a notification-area screen-capture app. After install, it starts in the tray (a startup
> task the user can turn off). Press Print Screen for a region capture, Shift+Print Screen for full
> screen, or Alt+Print Screen for the active window; Ctrl+Print Screen shows the floating toolbar.
> If Windows' "Use the Print Screen key to open screen capture" setting is on, Capta explains how to
> turn it off; it never changes that setting itself. Windows asks for screen-capture permission on
> the first capture.
>
> **runFullTrust**: a WinUI 3 desktop app. It needs a low-level keyboard hook to respond to the Print
> Screen key, and it needs topmost overlay and tray windows.
>
> **graphicsCaptureProgrammatic / graphicsCaptureWithoutBorder**: captures happen when the user presses
> the capture shortcut or clicks a capture button. Showing the system picker or a yellow border would
> defeat the purpose of a screenshot tool. Windows still asks the user for permission once.
>
> **Donations**: the Settings window links to buymeacoffee.com in the browser. It's a voluntary
> donation that unlocks nothing (Store Policy 10.8.2); no payment happens inside the app.

## 6. Store listing (English)

**Product name:** Capta

**Short description (≤ 100 characters):**
Fast, HDR-correct screenshots from the Print Screen key, right in your tray.

**Description:**

> Capta replaces the Print Screen key with a fast, precise capture tool that lives in your
> notification area.
>
> Press Print Screen and drag to capture a region. The screen freezes so menus and hover states stay
> put. A loupe shows the exact pixel and its colour, and you can hold Shift for a square or Space to
> move the selection. Shift+Print Screen captures the whole screen, Alt+Print Screen the active
> window, and Ctrl+Shift+Print Screen grabs the text under a selection.
>
> Captures look right on HDR displays. Capta captures in high dynamic range and tone-maps to
> standard range using your display's SDR white level, so screenshots aren't washed out or blown out.
>
> After each capture, a small card lets you copy, save, pin to the screen, edit, or copy the text
> in the image. Pinned captures float above your windows. You can zoom them, make them
> translucent, or click through them.
>
> - Region, window, full-screen and active-window capture
> - Frozen-screen overlay with a pixel loupe and colour picker (press C to pin a colour)
> - HDR-aware capture with tone-mapping you can adjust
> - Grab text: on-device text recognition, nothing uploaded
> - Pin captures above other windows
> - Capture delay of 3, 5 or 10 seconds
> - Rebindable shortcuts; light, dark and high-contrast themes
> - Recent captures one click away from the tray
>
> Capta collects no data and makes no network connections.

**What's new in this version:** First release.

**Features (up to 20, one line each):**
Region capture with frozen screen · Window and full-screen capture · Active-window capture ·
HDR-correct tone-mapping · Pixel loupe and colour picker · Grab text (on-device OCR) · Pin to screen ·
Capture delay · Rebindable Print Screen shortcuts · Recent captures in the tray

**Search terms (up to 7):** screenshot, screen capture, snipping, print screen, HDR screenshot,
OCR, pin screenshot

**Screenshots (1920×1080 or 2560×1440 PNG, at least one, up to ten):** take them with Capta's own
full-screen capture over a tidy desktop. Suggested set:

1. Region overlay mid-selection, with the loupe and size badge
2. After-capture card
3. Floating toolbar
4. A pinned capture with its hover bar, plus a colour chip
5. Tray flyout with Recent
6. Colour & HDR settings
7. Startup & shortcuts

**Store logos:** generated by `tools/generate_icons.py` from the Capta icon set. A 1:1 box art
image (1080×1080 or 2160×2160) is optional; render it from the same artwork.

## 7. Pricing and availability

Free, all markets, available to the public. Leave "Allow organizations to acquire" on.
