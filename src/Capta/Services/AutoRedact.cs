using Capta.Capture;

namespace Capta.Services;

/// <summary>
/// "Redact personal info": reads the capture's text on the device and covers emails, phone numbers
/// and long digit runs with a solid fill before the capture is copied, saved or sent anywhere. The card then offers Review
/// and Undo (see <see cref="Redaction"/>).
/// </summary>
/// <summary>How an attempt to redact a capture went.</summary>
public enum RedactOutcome
{
    /// <summary>Not looked for: the setting is off, or the capture isn't an image of the screen.</summary>
    Skipped,
    NothingFound,
    Redacted,
    /// <summary>The text couldn't be read (for example no OCR language is installed), so nothing was covered.</summary>
    Failed,
}

public static class AutoRedact
{
    /// <summary>
    /// Covers what's found in the capture's image and records it on <see cref="CaptureResult.Redaction"/>.
    /// Never throws: if OCR isn't available the capture is left as it was and the outcome is
    /// <see cref="RedactOutcome.Failed"/>, so the caller can say it wasn't checked.
    /// </summary>
    public static async Task<RedactOutcome> ApplyAsync(CaptureResult result)
    {
        if (!Settings.AutoRedact || result.IsVideo || result.Mode == CaptureMode.GrabText)
            return RedactOutcome.Skipped;
        try
        {
            var lines = await Ocr.RecognizeWordsAsync(result.Image);
            var matches = PersonalInfo.Find(lines);
            if (matches.Count == 0) return RedactOutcome.NothingFound;

            var redaction = new Redaction(result.Image, matches);
            result.Redaction = redaction;
            result.Image = await Task.Run(redaction.Render);
            Log.Info($"Redacted {PersonalInfo.Describe(matches)}");
            return RedactOutcome.Redacted;
        }
        catch (Exception ex)
        {
            Log.Info($"Personal info check failed: {ex.Message}");
            return RedactOutcome.Failed;
        }
    }
}
