using Capta.Capture;

namespace Capta.Services;

/// <summary>
/// "Redact personal info": reads the capture's text on the device and covers emails, phone numbers
/// and long digit runs with a solid fill before the capture is copied, saved or sent anywhere. The card then offers Review
/// and Undo (see <see cref="Redaction"/>).
/// </summary>
public static class AutoRedact
{
    /// <summary>
    /// Covers what's found in the capture's image and records it on <see cref="CaptureResult.Redaction"/>.
    /// Returns whether anything was redacted. Never throws: if OCR isn't available the capture is
    /// left as it was.
    /// </summary>
    public static async Task<bool> ApplyAsync(CaptureResult result)
    {
        if (!Settings.AutoRedact || result.IsVideo || result.Mode == CaptureMode.GrabText)
            return false;
        try
        {
            var lines = await Ocr.RecognizeWordsAsync(result.Image);
            var matches = PersonalInfo.Find(lines);
            if (matches.Count == 0) return false;

            var redaction = new Redaction(result.Image, matches);
            result.Redaction = redaction;
            result.Image = await Task.Run(redaction.Render);
            Log.Info($"Redacted {PersonalInfo.Describe(matches)}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Info($"Personal info check skipped: {ex.Message}");
            return false;
        }
    }
}
