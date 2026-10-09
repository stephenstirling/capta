# Capta → Ocula integration

Capta hands captures to Ocula, the Stirling image browser, through the `ocula:` URI scheme and
describes each capture in PNG metadata. This is the contract Ocula has to implement. Capta's side
lives in `src/Capta/Services/CaptureActions.cs` and `CaptureMetadata.cs`.

## Detecting Ocula

Capta treats Ocula as installed when `Launcher.QueryUriSupportAsync(new Uri("ocula:"), LaunchQuerySupportType.Uri)`
returns `Available`. Ocula must therefore declare the protocol in its package manifest:

```xml
<uap:Extension Category="windows.protocol">
  <uap:Protocol Name="ocula">
    <uap:DisplayName>Ocula</uap:DisplayName>
  </uap:Protocol>
</uap:Extension>
```

Until it does, Capta's **Edit** falls back to the default image app, and **Send to Ocula** and
**Open in Ocula** are disabled with the tooltip "Ocula isn't installed yet".

## URIs

`file` values are absolute Windows paths, percent-encoded with `Uri.EscapeDataString`.

| URI | Sent when | Ocula should |
| --- | --- | --- |
| `ocula:edit?file=<path>` | The card's or a pin's **Edit** button | Import the file (see below) and open it in the editor, in the foreground. |
| `ocula:import?file=<path>` | **Send to Ocula** on the card; also after every capture when "Send every capture to Ocula automatically" is on | Add the file to the library without stealing focus. Many can arrive in a row. |
| `ocula:captures` | **Open in Ocula** in the tray flyout | Open the library filtered to Capta captures (`12-ocula-captures-folder.png`). |

### The files are temporary

Capta writes these PNGs to its own `TempState` folder
(`%LOCALAPPDATA%\Packages\StephenStirling.Capta_*\TempState\Edit|Ocula\`). Windows may clear that
folder at any time. **Ocula must copy the file into its own library on import** and must not keep the
path. Ocula needs `runFullTrust` (normal for a WinUI 3 desktop app) to read another package's folder.

## PNG metadata

Saved PNGs (Save As, Edit, Send to Ocula, Copy as file) carry:

- an `sRGB` chunk (rendering intent perceptual). The pixels are always SDR sRGB, tone-mapped by
  Capta when the display was in HDR.
- a `tEXt` chunk with the keyword **`Capta`** whose text is one JSON object. The JSON is ASCII-only:
  non-ASCII characters are `\uXXXX`-escaped, because `tEXt` is Latin-1.

Images Capta puts on the clipboard as bitmaps carry no metadata.

```json
{
  "version": 1,
  "software": "Capta",
  "mode": "Region",
  "capturedAt": "2026-10-08T19:34:19.1234567-04:00",
  "sourceApp": "chrome",
  "windowTitle": "Network settings - Google Chrome",
  "width": 493,
  "height": 274,
  "toneMappedFromHdr": true,
  "ocrText": "Network\r\nWi-Fi\r\nProxy server"
}
```

| Field | Type | Notes |
| --- | --- | --- |
| `version` | int | `1`. Bumped only for breaking changes; new optional fields don't bump it. |
| `software` | string | Always `"Capta"`. |
| `mode` | string | `Region`, `Window`, `FullScreen`, `ActiveWindow`, `GrabText`, `Freeform` (transparent outside the drawn shape) or `Scrolling` (stitched, usually very tall). |
| `capturedAt` | string | ISO 8601 with offset (local time). |
| `sourceApp` | string? | Process name of the captured window. For regions, it's the window under the selection's centre. Absent when unknown (e.g. full screen). |
| `windowTitle` | string? | That window's title. Absent when unknown. |
| `width`, `height` | int | Pixels. |
| `toneMappedFromHdr` | bool | The source display was in HDR. |
| `ocrText` | string? | On-device OCR (Windows.Media.Ocr, the user's languages), lines joined by CRLF. Absent when no text was found. |

Ocula should ignore unknown fields and treat every optional field as possibly absent.
