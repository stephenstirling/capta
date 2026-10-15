# Capta design reference

This folder is the source of truth for how Capta looks and behaves. When building or changing UI, match these screens, not default WinUI styling. Updated 2026-10-10.

## What's here

- `screenshots/*.png`: rendered pictures of every approved screen at 1:1 size. **Look at these first.** They show the target exactly.
- `mockups/*.html`: the same screens as source. Every size, colour, radius and gap is an inline `style=""`, so read them for exact numbers. They need a canvas runtime to render, so treat them as specs, not pages to open. `{{accent}}` means `#FFB547`; `{{accentSoft}}` is the accent at about 17% opacity (`CaptaAccentSoftBrush`).
- `icons/`: the chosen "Aperture" icon as SVG, PNG at every size and `Capta.ico`, plus white and black 16 px tray icons (white for dark taskbars, black for light).

Colours, shapes and fonts are **not** here. They live in one place only: `src/Stirling.Shared/Themes/Colors.xaml`. Use its `Capta*` brushes and tokens; never hard-code a colour.

## Screen → screenshot → mockup → code

| Screen | Screenshot | Mockup | Code | Status |
|---|---|---|---|---|
| Floating toolbar | `02-toolbar-and-card.png` (top) | `Toolbar.html` | `Views/ToolbarWindow.xaml` | Built |
| After-capture card | `02-toolbar-and-card.png` (bottom right) | `Toolbar.html` | `Views/CaptureCardWindow.xaml` | Built (plus a video mode for recordings) |
| Copy menu (open) | `01-toolbar-with-copy-menu.png` | `Toolbar.html` | `CaptureCardWindow` flyout | Built |
| Capture overlay | `03-capture-overlay.png` | `Main.html` | `Overlay/OverlayWindow.xaml` | Built (settings button opens a Capture settings menu) |
| Pin to screen | `04-pin-to-screen.png` | `Pinned.html` | `Views/PinWindow.xaml` | Built |
| Tray flyout | `05-tray-flyout.png` | `Tray.html` | `Views/TrayFlyoutWindow.xaml` (left click); `Services/TrayIconService.cs` (right-click menu) | Built |
| Startup & shortcuts | `06-startup-and-shortcuts.png` | `Startup.html` | `Views/StartupSettingsWindow.xaml` | Built |
| Colour & HDR settings | `07-colour-and-hdr.png` | `Hdr.html` | `Views/ColourHdrWindow.xaml` | Built (monitor correction off by default) |
| First-run setup | `08-first-run.png` | `FirstRun.html` | `Views/FirstRunWindow.xaml` | Built |
| App icon | `09-app-icon.png` | `Icon.html` | `Stirling.Shared/Assets/Capta` | Done (concept A) |
| Editor, dark / light | `10-…`, `11-…` | `Editor.html`, `EditorLight.html` | **not in Capta** | Future Ocula editing view |
| Ocula captures folder | `12-ocula-captures-folder.png` | `Ocula.html` | **not in Capta** | For Ocula |

Mockup details that are scenery, not UI to build: the grey placeholder windows and taskbar behind each screen, and the yellow "Display 1 is in HDR" tooltip (a hover tooltip on the HDR badge).

## Decisions (2026-10-07)

1. **Print Screen shortcuts.** These override `Startup.html` where they differ:

   | Keys | Action |
   |---|---|
   | PrtSc | Region capture |
   | Shift+PrtSc | Full screen, straight to the clipboard |
   | Alt+PrtSc | Active window |
   | Ctrl+PrtSc | Record video: choose an area, then Ctrl+PrtSc or Stop to finish. The floating toolbar is in the tray menu. |
   | Ctrl+Shift+PrtSc | Grab text from screen |

2. **Region and Grab text capture when the drag ends**, like Snipping Tool, and copy with Auto-copy on. Only
   Scrolling and Record keep an adjustable selection with handles, confirmed with Enter, since finishing starts
   something long-running. The overlay's Enter hint shows only in those modes.
3. **Edit** opens `ocula:edit?file=…` if Ocula is registered, otherwise the default image app. Button label and tooltip say just "Edit". Capta does not get its own editor; `Editor.html` is reference for Ocula.
4. **One theme file:** `src/Stirling.Shared/Themes/Colors.xaml`. There is no theme file in `design/`.

## Visual rules

- **Fonts:** Segoe UI Variable Text for UI; Cascadia Mono for sizes, coordinates, hex values and shortcut keys.
- **Type:** section labels 11 px bold uppercase, letter spacing 0.08em, muted colour. Body 12.5–13.5 px. Dialog titles 20 px bold. First-run headings 26–30 px extra bold.
- **Accent:** amber `#FFB547` with `#1A1206` on top. Used for the primary action, the active tool, toggles that are on, and selection outlines. In the light theme, accent-coloured text and icons use `CaptaAccentTextBrush`.
- **Surfaces:** solid colours, not Mica. The floating toolbar and after-capture card use a near-opaque dark surface (`rgba(24,27,32,0.96)`) with a 1 px `#343A44` border and a soft shadow; Acrylic is acceptable if it looks the same.
- **Corners:** windows 14 px, cards 12, buttons 10–11, floating toolbar and after-capture card 16, pills fully round.
- **Hit targets:** icon buttons 40–44 px square. Regular buttons 40 px tall.
- **Icons:** 1.8 px stroke line icons at about 18 px, matching the inline SVGs. Segoe Fluent Icons are fine where a close match exists.

## Per-screen checklist

**Floating toolbar** (one row, 6 px padding, 16 px corners)
drag grip · amber **New** button (Aperture glyph + "New") · dark inset group of icon-only mode buttons (Region active: soft-amber fill, amber icon; Window, Full screen, Freeform, Scrolling, Record) · divider · Delay dropdown ("Off") · Auto-copy toggle · "HDR → SDR" badge (shown only when a display is in HDR) · divider · keep-on-top (amber when on) · settings · hide to tray · close.

**After-capture card** (bottom right, 400 px wide, 14 px padding, 16 px corners)
Row 1: 96 × 60 thumbnail · green dot + "Copied to clipboard" (bold 14 px) with a muted line under it ("Region · 680 × 430 · tone-mapped from HDR") · dismiss ×.
Row 2: **Copy** split button (Copy + chevron opening the menu) · **Edit**, wide amber primary · Save, Pin, Send to Ocula as 44 × 40 icon buttons.
Copy menu: Copy image (Ctrl+C, checked default) · Copy image as HDR · Copy text in image (Ctrl+Shift+C) · Copy as file · Copy file path · separator · "Copy automatically after capture" toggle.

**Capture overlay**
Dim `CaptaScrimBrush` outside the selection · 2 px amber frame · 8 white square handles with amber borders · thirds guides at 22% white · amber size badge with dark mono text above the top-left corner · circular 132 px loupe with white border, magnified pixels, amber crosshair and a centre-pixel box · readout pill below the loupe (coordinates · colour swatch · hex) · mode bar at the top matching the toolbar's modes · key hints under it (Space move, Shift square, Enter capture, Esc cancel).

**Pin window**
Amber 2 px outline with offset and a deep shadow · resize corner mark · on hover, a bar above the top-right with opacity slider and %, click-through, copy, edit, unpin · hint text "Scroll to zoom · drag corner to resize · double-click to unpin".

## Behaviour the screens assume

- Starts with Windows (`windows.startupTask`) and starts hidden in the tray; the toolbar appears only on request.
- Print Screen via a `WH_KEYBOARD_LL` hook. If `HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled` is 1, show the warning card and open `ms-settings:easeofaccess-keyboard`; never write that value from the packaged app.
- After capture, copy an SDR PNG to the clipboard automatically (on by default), then show the card.
- HDR: capture FP16 via Windows.Graphics.Capture, scale by `AdvancedColorInfo.SdrWhiteLevelInNits`, tone-map highlights, convert to sRGB.
- Pins are separate borderless always-on-top windows; click-through uses `WS_EX_LAYERED | WS_EX_TRANSPARENT`.
- Saved PNGs carry source app, window title, capture mode and OCR text in metadata for Ocula.
