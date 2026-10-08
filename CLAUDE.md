# Capta

WinUI 3 (Windows App SDK, .NET 10) screen-capture tray app, packaged as MSIX for the Microsoft Store.
See README.md for layout and build commands.

## Before any UI work

1. Read `design/DESIGN.md` first. Its "Decisions" section overrides the mockups where they differ.
2. For each window, open its PNG in `design/screenshots` with the Read tool and look at it before writing
   XAML. Use `design/mockups/*.html` for exact numbers: the inline `style=""` values give sizes, radii, gaps
   and colours. `{{accent}}` means `#FFB547`.
3. `src/Stirling.Shared/Themes/Colors.xaml` is the only theme file. Use its brushes and tokens
   (`Capta*Brush`, `CaptaRadius*`, `CaptaFont`, …); never hard-code colours.

## Theme: one source of truth

- All colour, shape and font tokens live in `src/Stirling.Shared/Themes/Colors.xaml`, merged once in `App.xaml`.
  Add or change tokens there; don't add a second resource dictionary.
- If a mockup uses a colour that has no token yet, add a token to `Colors.xaml` (both themes) rather than
  hard-coding it.
- Reference brushes with `{ThemeResource …}` so Light, Dark and High Contrast all switch correctly.
- For code that draws pixels outside XAML, use `Stirling.Shared.StirlingColors`, which mirrors the tokens.

## Building and running

- Build: `msbuild Capta.slnx -restore -p:Configuration=Debug -p:Platform=x64`.
- MSIX packages can't be registered from a network share. On a mapped drive, put a git-ignored
  `Directory.Build.user.props` at the repo root that sets `StirlingLocalBuildRoot` (see `Directory.Build.props`).
- Diagnostics: `%LOCALAPPDATA%\Packages\StephenStirling.Capta_*\LocalState\capta.log`.
