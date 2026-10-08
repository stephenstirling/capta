# Capta

Fast, HDR-correct screen capture for Windows 11. Capta lives in the notification area,
takes over the Print Screen key and copies captures to the clipboard as PNG.

## Shortcuts

| Keys | Action |
| --- | --- |
| PrtSc | Region capture |
| Shift+PrtSc | Full screen, straight to the clipboard |
| Alt+PrtSc | Active window |
| Ctrl+PrtSc | Show the floating toolbar (will become Record video) |
| Ctrl+Shift+PrtSc | Grab text from screen: select a region and its text is copied |

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

## Store identity

`Package.appxmanifest` uses a placeholder identity (`StephenStirling.Capta`, `CN=StephenStirling`).
Before submitting, run **Project → Publish → Associate App with the Store** in Visual Studio.

## Support

Capta is made by [Stephen Stirling](https://github.com/stephenstirling). If it's useful to you,
you can [buy me a coffee](https://buymeacoffee.com/stephenstirling).

## Licence

Apache-2.0. See [LICENSE](LICENSE).
