# Capta

Fast, HDR-correct screen capture for Windows 11. Capta lives in the notification area,
takes over the Print Screen key and copies captures to the clipboard as PNG.

## Shortcuts

| Keys | Action |
| --- | --- |
| PrtSc | Region capture |
| Shift+PrtSc | Full screen, straight to the clipboard |
| Alt+PrtSc | Active window |
| Ctrl+PrtSc | Record video: choose an area, then Ctrl+PrtSc or Stop to finish |
| Ctrl+Shift+PrtSc | Grab text from screen: select a region and its text is copied |

In the region overlay, **Esc** captures the selection once you've drawn one (and copies it, with Auto-copy on);
with nothing selected it cancels. Press **C** to pick the colour under the cursor: its hex code is copied and
pinned as a small chip (click the code to copy again, double-click or Esc to unpin). The settings button at
the end of the overlay's bar switches Auto-copy and opens the settings windows.

**Colour & HDR** (tray menu) sets how HDR screens are tone-mapped, and whether saved files are sRGB or
Display P3 (which keeps wide-gamut colours and embeds its profile; the clipboard always gets sRGB). It can also
correct captures of an SDR monitor for its colour profile, so they look as they did on a calibrated or
wide-gamut screen.

Recordings are MP4 (H.264 + AAC) in your capture folder, with system audio and, if you switch it on,
the microphone. The card after a recording can play it, copy the file or save a GIF of the first
30 seconds. The floating toolbar is in the tray menu.

Win+PrtSc and Win+Shift+S stay with Windows. If Windows is set to open Snipping Tool on Print Screen,
Capta shows how to turn that off in Settings.

## Requirements

- Windows 11 22H2 (10.0.22621) or later
- Visual Studio 2026 with the **WinUI application development** workload, or the .NET 10 SDK
- Developer Mode enabled (to deploy the unsigned debug package)

## Build

```powershell
msbuild Capta.slnx -restore -p:Configuration=Debug -p:Platform=x64
```

Open `Capta.slnx` in Visual Studio and press F5 to deploy and run the packaged app.

## Layout

| Path | Purpose |
| --- | --- |
| `src/Capta` | The WinUI 3 app, packaged as MSIX (single-project packaging). |
| `src/Stirling.Shared` | Brand resources shared with other Stirling apps (e.g. Ocula): theme colours and app icons. |
| `tools/generate_icons.py` | Regenerates the logo/ico set in `src/Stirling.Shared/Assets/<App>`. |

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

## Store identity

`Package.appxmanifest` uses a placeholder identity (`StephenStirling.Capta`, `CN=StephenStirling`).
Before submitting, run **Project → Publish → Associate App with the Store** in Visual Studio.

## Support

Capta is made by [Stephen Stirling](https://github.com/stephenstirling). If it's useful to you,
you can [buy me a coffee](https://buymeacoffee.com/stephenstirling).

## Licence

Apache-2.0. See [LICENSE](LICENSE).
