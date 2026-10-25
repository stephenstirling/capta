# Release checklist: try it on a PC

CI builds Capta and runs the unit tests, but nobody has seen the windows. Before tagging a release, install a
**Release** build (it's trimmed, so it can behave differently from Debug) on a Windows 11 PC and go through this.
Tick it off in a copy of this file or an issue. For 1.1.0 the new things are marked ★.

## Install

- [ ] Build or download the package. Unsigned: `Add-AppxPackage -Register` from a Debug build with Developer Mode on.
      Signed: `Get-AuthenticodeSignature .\Capta_*_x64_arm64.msixbundle` says `Valid` with SignPath Foundation as the
      signer, then double-click it (or `Add-AppxPackage`).
- [ ] Capta starts in the tray and the version in Settings → About is the release version.
- [ ] Update an installed older build in place (settings and Recent captures survive).

## Capture

- [ ] Print Screen: region, with the loupe; the capture is taken when the drag ends and is on the clipboard.
- [ ] Shift+PrtSc full screen; Alt+PrtSc active window; Ctrl+Shift+PrtSc grab text; Ctrl+PrtSc record, again to stop.
- [ ] ★ Window and Active window captures have transparent rounded corners (paste into an app with a coloured
      background, or open the saved PNG on one).
- [ ] ★ Settings → Include cursor in screenshots: off, no cursor; on, the cursor is in full-screen and window
      captures, and never in a region capture.
- [ ] ★ Repeat last region: Ctrl+Alt+PrtSc, the tray item and the toolbar button all capture the same area; the
      tray item and button are greyed before any region exists.
- [ ] HDR display, if you have one: a capture is tone-mapped; Copy as HDR pastes into an HDR-aware app.

## After a capture

- [ ] The card: Copy, Edit, Save, Pin, Send to Ocula (or Get Ocula once `Links.OculaStore` is set).
- [ ] ★ Copy menu → Copy for chat: the card says "Copied for chat · W × H · N KB"; paste into a chat app and a
      big capture is at most 1568 px on its long edge. A busy full-screen capture over 1 MB pastes as a JPEG.
- [ ] ★ Overlay menu (settings button on the overlay's bar) → Copy for chat makes Auto-copy use it.
- [ ] ★ Settings → Redact personal info on, then capture text containing an email, a phone number and a 16-digit
      number: each is covered with black before pasting. The card shows "Personal info found" with Review (a switch
      per item) and Undo; Undo restores the clipboard image. Off by default.
- [ ] Pin a capture; zoom, opacity, click-through; "Make pinned captures clickable" appears in the tray menu.

## ★ Tray menu and windows

- [ ] Right-click the tray icon: compact 32 px rows, one icon column, shortcut hints beside the labels, order as in
      DESIGN.md decision 8. Esc and clicking elsewhere close it. Check dark and light themes.
- [ ] "Print Screen still opens Snipping Tool" + Fix shows while Windows owns Print Screen, and wraps rather than
      truncating.
- [ ] Start with Windows shows its tick in the icon column and toggles. Settings… opens Startup & shortcuts, with a
      Colour & HDR link. About Capta… opens it at the About section. Set up Capta… only appears before first-run
      setup is done.
- [ ] Shadows on the toolbar, card, recording bar and tray menu are light and don't clip at the window edge.
- [ ] Multi-monitor and a high-DPI monitor: menu and card open on the right screen and stay on it.

## ★ Updates (website build)

- [ ] Settings → Updates: the switch and Check for updates are there. Before a release is published, Check now says
      no release was found. After one is published and is newer than the installed version, the card says
      "Version 1.x.y is available" and What's new / Download open the release page in the browser.
- [ ] Nothing is downloaded or installed by Capta; the switch off stops the daily check.
- [ ] Store build: no Updates row in the tray menu, and Settings says "Updates come from the Microsoft Store".

## Release

- [ ] `CHANGELOG.md` and `Package.appxmanifest` agree on the version; **Build** is green on `main`.
- [ ] Tag from `main`: `git tag vX.Y.Z && git push origin vX.Y.Z`. Review the draft release, add the changelog text and
      the SignPath attribution line, publish.
- [ ] After publishing, an older installed build finds it with Check for updates.
