# Capta privacy policy

_Effective 8 October 2026_

Capta is a screen-capture app for Windows made by Stephen Stirling. It works entirely on your PC.

## What Capta collects

**Nothing.** Capta has no accounts, analytics, telemetry, advertising or crash reporting. It makes no
network connections. The GitHub and Buy Me a Coffee links open in your browser only when you click them.

## What stays on your PC

Capta keeps this data on your device only, in its own app folder
(`%LOCALAPPDATA%\Packages\StephenStirling.Capta_*`):

- **Settings**: shortcuts, theme, capture and colour options.
- **Recent captures**: your six most recent captures, as PNG files (for recordings, a still frame),
  so the tray menu can show them after a restart. Older ones are deleted automatically.
- **Temporary files**: PNGs written when you use Edit, Copy as file or Send to Ocula. Windows clears
  these from time to time.
- **A diagnostic log** (`capta.log`): timings, image sizes and errors. It doesn't contain the content
  of your captures, window titles or recognised text.

Uninstalling Capta removes all of this.

## Your captures

- Captures go to the **clipboard** (if Auto-copy is on) and to files **you** choose to save.
- **Text recognition** (Grab text, Copy text, and the metadata below) uses Windows' built-in OCR on
  your PC. Images are never uploaded.
- **Saved PNGs include metadata** describing the capture: the capture mode and time, the name and
  title of the app window that was captured, the image size, and any text recognised in the image.
  This lets image apps such as Ocula search your captures. If you share a saved PNG, this metadata
  goes with it. Images copied to the clipboard as pictures have no metadata.

## Video recordings and the microphone

Recordings are saved as MP4 files in your capture folder (Pictures\Captures by default). They
include what your PC plays (system audio) unless you turn that off on the recording bar.
The microphone is used only while you have it switched on during a recording. Windows asks for
microphone permission the first time, and shows its microphone indicator while it's in use.
Nothing is uploaded.

## Screen capture permission

Windows asks before Capta first captures your screen. You can withdraw this permission at any time
in Windows Settings, under **Privacy & security**.

## Children

Capta doesn't collect personal information from anyone, including children.

## Changes

Changes to this policy will be published at this address with a new effective date.

## Contact

Questions: open an issue at <https://github.com/stephenstirling/capta/issues>.
