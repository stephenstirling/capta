# Capta design reference

This folder is the source of truth for how Capta should look and behave. When building UI, match these mockups rather than default WinUI styling.

## What's here

- `mockups/*.html` are the approved screens. Every size, colour, radius and gap is written as an inline `style=""` on the element, so read the HTML to get exact values. They need the canvas runtime to render, so don't expect them to open in a browser; read them as specs. The `{{accent}}` placeholders mean the accent colour `#FFB547`.
- `xaml/CaptaTheme.xaml` holds the same tokens as a WinUI resource dictionary, with Light and Dark versions. Use these brushes instead of hard-coded colours.
- `icons/` holds the chosen "Aperture" app icon as SVGs, PNGs at every size, `Capta.ico`, and single-colour tray icons (white for dark taskbars, black for light).

## Screens and their mockup files

| Screen | File | Notes |
|---|---|---|
| Floating toolbar + after-capture card | `Toolbar.html` | The entry point. Small always-on-top bar; after a capture, a card appears bottom-right with Copy (split button with menu), Edit, Save, Pin, Send to Ocula |
| Colour & HDR settings | `Hdr.html` | HDR handling choice, SDR white level, highlight roll-off, colour space |
| Startup & shortcuts | `Startup.html` | Start with Windows, tray behaviour, Print Screen status card, shortcut list |
| Capture overlay | `Main.html` | Dimmed screen, selection with handles and thirds guides, size badge above, magnifier loupe with coordinates and hex, mode bar at top |
| Editor (dark) | `Editor.html` | Title bar, tool bar, canvas with dot grid, 340 px side panel |
| Editor (light) | `EditorLight.html` | Same layout in the light theme |
| Ocula smart folder | `Ocula.html` | How captures appear inside Ocula |
| Tray flyout | `Tray.html` | 380 × 560 flyout from the tray icon |
| Pin to screen | `Pinned.html` | Borderless always-on-top capture with opacity, click-through, copy, edit, unpin |
| App icon | `Icon.html` | Concept A ("Aperture") is the chosen icon |
| First-run setup | `FirstRun.html` | Five steps: welcome, Print Screen, startup + theme, Ocula, done |

## Visual rules

- **Font:** Segoe UI Variable Text for UI, Cascadia Mono for numbers like sizes, coordinates and hex values.
- **Type sizes:** section labels are 11 px, bold, uppercase with 0.08em letter spacing, in the muted colour. Body text is 12.5–13.5 px. Window titles are 20–22 px bold. First-run headings are 26–30 px extra bold.
- **Accent:** amber `#FFB547`, with near-black `#1A1206` text on top of it. Use it for the primary action, the active tool, toggles that are on, and selection outlines. In the light theme, accent-coloured text and icons use `CaptaAccentTextColor` instead.
- **Surfaces:** solid colours, not Mica or Acrylic, so screenshots of the app look the same everywhere. The floating toolbar and the after-capture card are the exception and can use a slightly translucent surface.
- **Shapes:** windows 14 px corners, cards 12, buttons 10, the floating toolbar 16, filter chips fully rounded.
- **Hit targets:** icon buttons are 44 × 44 px. Regular buttons are 40 px tall.
- **Icons:** 1.8 px stroke line icons, around 18 px, matching the inline SVGs in the mockups. Segoe Fluent Icons are an acceptable stand-in where an exact match exists.
- **Title bar:** custom, using `ExtendsContentIntoTitleBar`. It's 40 px tall with the Aperture glyph, "Capta" and the file name on the left.

## Behaviour that the mockups assume

- **Startup:** Capta starts with Windows via the MSIX `windows.startupTask` and starts hidden in the tray. The floating toolbar only appears when asked for.
- **Print Screen:** catch `VK_SNAPSHOT` (plus Shift, Alt, Ctrl variants) with a `WH_KEYBOARD_LL` hook. If `HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled` is 1, show the warning card and open `ms-settings:easeofaccess-keyboard`. Don't write that value directly from the packaged app.
- **After capture:** copy to the clipboard automatically as an SDR PNG (setting on by default), then show the after-capture card. The editor only opens from Edit.
- **HDR:** capture in FP16 via Windows.Graphics.Capture, scale by `AdvancedColorInfo.SdrWhiteLevelInNits`, tone-map highlights, and convert to sRGB.
- **Auto-redact:** after on-device OCR, blur emails, phone numbers and long digit runs, and show the "Personal info found" card with Review and Undo.
- **Pin:** each pin is its own borderless always-on-top window. Click-through uses `WS_EX_LAYERED | WS_EX_TRANSPARENT`.
- **Ocula:** saved PNGs carry source app, window title, capture mode and OCR text in metadata so Ocula can build its "All captures" smart folder.
