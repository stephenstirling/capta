using Capta.Capture;
using Windows.Media.Ocr;

namespace Capta.Services;

/// <summary>On-device text recognition (Windows.Media.Ocr, the user's profile languages).</summary>
public static class Ocr
{
    /// <summary>The recognised text, one line per line, or null if there is none.</summary>
    public static async Task<string?> RecognizeAsync(CapturedImage image)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("No OCR language is installed. Add one in Settings > Time & language.");
        using var bitmap = image.ToSoftwareBitmap();
        var result = await engine.RecognizeAsync(bitmap);
        var text = string.Join(Environment.NewLine, result.Lines.Select(l => l.Text));
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
