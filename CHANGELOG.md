# Changelog

## Unreleased

- **Save text found in captures (optional):** a switch in Settings, in the Capture section, to stop Capta saving the
  text it reads from a capture inside the PNG. It's on by default, as before. With it off Capta doesn't run OCR for
  saved files and the PNG carries no recognised text, so Ocula has nothing to search. Grab text, Copy text and Redact
  personal info are unaffected.

## 1.1.0

- **Copy for chat:** put a smaller copy of a capture on the clipboard (long edge at most 1568 px, a JPEG if
  the PNG would be over 1 MB), ready to paste into AI chats and messaging apps.
- **Redact personal info (optional):** email addresses, phone numbers and card-like numbers found in a capture
  are covered with a solid fill before it's copied, with Review and Undo on the card. Off by default.
- **Repeat last region:** capture the same area again with Ctrl+Alt+Print Screen, from the tray menu or from
  the toolbar.
- **Window captures keep their rounded corners**, with a transparent background.
- **Include the mouse cursor** in screenshots (off by default).
- **Update check** for the website build: Capta looks for new releases on GitHub once a day and shows a card
  when one is available. Store builds get updates from the Microsoft Store and make no network connections.
- **About** section in Settings with the version, licence and links.
- **Get Ocula:** when Ocula isn't installed, Capta offers its Store page instead of a disabled button.
- A tidier tray menu: compact rows, shortcuts next to their names, and one Settings entry.
- Lighter shadows on the toolbar, capture card, recording bar and tray windows.
- The capture overlay's mode bar and key hints fade out while you drag a selection, so they can't cover it.
- **Theme setting:** Startup & shortcuts has an Appearance section to switch between System (follows Windows),
  Light and Dark at any time. Until now the theme could only be chosen during first-run setup.
- **Print Screen from keyboard software:** Capta now acts on Print Screen sent by keyboard utilities, such as a
  Logitech MX Keys screenshot key set to "Print screen" in Logi Options+, and by the on-screen keyboard. It still
  ignores every other key sent by software.

## 1.0.0

First release.

### Capture

- Capta lives in the notification area and takes over the Print Screen key: Print Screen for a region,
  Shift for full screen, Alt for the active window, Ctrl to record video, Ctrl+Shift to grab text.
  Shortcuts can be changed in Settings. Win+Print Screen and Win+Shift+S stay with Windows.
- Region capture on a frozen screen, with a pixel loupe, size badge, handles, Space to move and Shift for
  a square. The capture is taken when the drag ends.
- Window, full-screen, active-window, freeform (transparent outside the shape) and scrolling capture.
- Grab text: select a region and its text is copied, using on-device OCR.
- Capture delay of 3, 5 or 10 seconds.
- Press C in the overlay to copy the colour under the cursor and pin it as a colour chip.

### HDR and colour

- HDR-correct captures: the screen is captured in high dynamic range and tone-mapped to SDR using the
  display's SDR white level, with a choice of highlight roll-off.
- Copy as HDR (JPEG XR), and options to keep HDR or save both when saving.
- Save in sRGB or Display P3, and optionally correct for each monitor's colour profile.

### After a capture

- A card with Copy, Edit, Save, Pin and Send to Ocula, plus Copy text, Copy as file and Copy file path.
- Pin captures above other windows: zoom, opacity, click-through.
- Saved PNGs carry capture metadata (app, window title, mode, recognised text) for Ocula.
- Recent captures in the tray, kept across restarts.

### Video

- Screen recording to MP4 (H.264 and AAC) with system audio and an optional microphone, a recording bar
  with a timer, and GIF export from the card.

### App

- Floating toolbar, tray flyout and tray menu; light, dark and high-contrast themes.
- First-run setup, start with Windows, and settings for startup, shortcuts, colour and HDR.
