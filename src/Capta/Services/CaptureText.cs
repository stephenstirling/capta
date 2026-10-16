namespace Capta.Services;

/// <summary>
/// The text found in a capture that is saved in its PNG metadata (<see cref="CaptureMetadata.OcrText"/>), for apps such as
/// Ocula to search. Whether to save it is the "Save text found in captures" setting; when it's off the text is never read,
/// so a saved PNG carries no <c>ocrText</c> at all.
/// </summary>
public static class CaptureText
{
    /// <summary>
    /// The text to save, or null. When <paramref name="save"/> is false <paramref name="recognise"/> is not called, so no OCR
    /// runs for the file. If recognising fails the capture is still saved, without text.
    /// </summary>
    public static async Task<string?> ForMetadataAsync(bool save, Func<Task<string?>> recognise)
    {
        if (!save) return null;
        try
        {
            return await recognise();
        }
        catch (Exception ex)
        {
            Log.Info($"OCR for metadata skipped: {ex.Message}");
            return null;
        }
    }
}
