# Microsoft Store submission

A checklist and draft listing for submitting Capta in Partner Center.

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

The bundle (`Capta_<version>_x64_arm64.msixbundle`, plus the `.msixupload` to submit) lands under
`bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\Upload\` (inside `StirlingLocalBuildRoot` when
that's set). The x64 + ARM64 bundle is about 66 MB. Release builds are self-contained and partially trimmed, about 34 MB per architecture.
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
> screen, or Alt+Print Screen for the active window; Ctrl+Print Screen records video (again to stop).
> If Windows' "Use the Print Screen key to open screen capture" setting is on, Capta explains how to
> turn it off; it never changes that setting itself. Windows asks for screen-capture permission on
> the first capture.
>
> **runFullTrust**: a WinUI 3 desktop app. It needs a low-level keyboard hook to respond to the Print
> Screen key, and it needs topmost overlay and tray windows.
>
> **microphone**: used only when the user switches the microphone on while recording a video, to
> narrate it. It's off by default.
>
> **graphicsCaptureProgrammatic / graphicsCaptureWithoutBorder**: captures happen when the user presses
> the capture shortcut or clicks a capture button. Showing the system picker or a yellow border would
> defeat the purpose of a screenshot tool. Windows still asks the user for permission once.
>
> **Donations**: the Settings window links to buymeacoffee.com in the browser. It's a voluntary
> donation that unlocks nothing (Store Policy 10.8.2); no payment happens inside the app.

## 6. Store listing (English)

Limits, from [Microsoft's listing guide](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info):
the description takes up to 10,000 characters of plain text (no HTML, code or URLs); **What's new** 1,500; up to
20 features, each up to 200 characters, **entered one per field**; the short description 1,000, with only the first
270 shown; at least one screenshot, four or more recommended. Every field below fits: the short description is
150 characters, the description 1,841, and there are 18 features, the longest 76 characters.

**Product name:** Capta

**Short description** (keep under 270):

> Press Print Screen, drag, done. Capta captures regions, windows, scrolling pages and video, and keeps HDR screens
> looking right. Free and open source.

**Description** (plain text, 1,841 characters):

> Capta replaces the Print Screen key with a fast, precise capture tool that lives in your notification area.
>
> Press Print Screen and drag to capture a region. The screen freezes so menus and hover states stay put, a loupe
> shows the exact pixel and its colour, and the capture is taken the moment you let go. Shift+Print Screen captures
> the whole screen, Alt+Print Screen the active window, Ctrl+Print Screen records video, and Ctrl+Shift+Print Screen
> grabs the text under a selection.
>
> Captures look right on HDR displays. Capta captures in high dynamic range and tone-maps to standard range using
> your display's SDR white level, so screenshots aren't washed out or blown out.
>
> After each capture, a small card lets you copy, save, pin to the screen, edit, or copy the text in the image.
> Pinned captures float above your windows: zoom them, make them translucent, or click through them. "Copy for
> chat" puts a smaller copy on the clipboard for AI chats and messaging apps. An optional setting covers email
> addresses, phone numbers and card-like numbers before a capture is copied, with Review and Undo.
>
> - Region, window, full-screen, active-window, freeform and scrolling capture
> - Screen recording to MP4 with system audio and microphone, plus GIF export
> - Frozen-screen overlay with a pixel loupe and colour picker
> - HDR-aware capture with tone-mapping you can adjust, and Copy as HDR (JPEG XR)
> - Save in sRGB or Display P3, with optional correction for your monitor's colour profile
> - Grab text: on-device text recognition, nothing uploaded
> - Repeat the last region from a shortcut, the tray or the toolbar
> - Capture delay of 3, 5 or 10 seconds
> - Rebindable shortcuts; light, dark and high-contrast themes
> - Recent captures one click away from the tray
>
> Capta is free and open source. It collects no data and makes no network connections.

**What's new in this version:** leave it **blank**. Microsoft says to leave it empty the first time an app is
submitted, and this is the first Store submission (1.1.0). For later updates, paste that version's lines from
`CHANGELOG.md` (up to 1,500 characters).

**Features** (18 of 20; enter each as its own item, no bullets):

1. Region capture on a frozen screen with a pixel loupe
2. Window, full-screen and active-window capture
3. Freeform and scrolling capture
4. Screen recording to MP4 with system audio and microphone
5. GIF export for recordings
6. HDR-correct tone-mapping with adjustable highlights
7. Copy as HDR (JPEG XR) for HDR-aware apps
8. sRGB or Display P3, with optional monitor colour correction
9. Grab text: on-device text recognition, nothing uploaded
10. Pin captures above other windows
11. Colour picker that pins a colour chip
12. Copy for chat: a smaller copy for AI chats and messaging apps
13. Optional redaction of emails, phone numbers and card-like numbers, with Undo
14. Repeat the last region from a shortcut, the tray or the toolbar
15. Capture delay of 3, 5 or 10 seconds
16. Rebindable Print Screen shortcuts
17. Light, dark and high-contrast themes
18. Recent captures one click away from the tray

**Search terms:** screenshot, screen recorder, screen capture, print screen, HDR screenshot, OCR, scrolling
screenshot. (The page I checked doesn't state the term limits; Partner Center shows them when you enter these.)

**Screenshots (1920×1080 or 2560×1440 PNG, at least one, up to ten):** take them over a tidy desktop. Suggested set:

1. Region overlay mid-selection, with the loupe and size badge
2. After-capture card
3. Floating toolbar
4. A pinned capture with its hover bar, plus a colour chip
5. Tray flyout with Recent
6. Colour & HDR settings
7. Startup & shortcuts (including Appearance)
8. The card with "Personal info found"

**Store logos:** generated by `tools/generate_icons.py` from the Capta icon set. A 1:1 box art
image (1080×1080 or 2160×2160) is optional; render it from the same artwork.

## 7. Notes for the first submission (1.1.0)

`Package.appxmanifest` is at version 1.1.0.0.

- **Store builds make no network connections.** The update check in 1.1 is for the website build only: it's
  hidden when Capta runs from the Store (`Package.Current.SignatureKind == Store`), which says "Updates come
  from the Microsoft Store". The product declarations and privacy policy stay as they are for the Store.
- Before uploading, test: Copy for chat from the card's Copy menu, the redaction card (turn the setting on
  first), and Repeat last region from the toolbar.

## 8. Pricing and availability

Free, all markets, available to the public. Leave "Allow organizations to acquire" on.
