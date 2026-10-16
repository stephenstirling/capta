using System.Text.Json;
using System.Text.Json.Serialization;
using Capta.Capture;

namespace Capta.Services;

/// <summary>
/// Details written into saved PNGs so Ocula can index captures (DESIGN.md: source app,
/// window title, capture mode and OCR text). Stored as one PNG tEXt chunk with the keyword
/// "Capta", holding ASCII-only JSON (non-ASCII is \u-escaped).
/// </summary>
public sealed record CaptureMetadata(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("software")] string Software,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("capturedAt")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("sourceApp")] string? SourceApp,
    [property: JsonPropertyName("windowTitle")] string? WindowTitle,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("toneMappedFromHdr")] bool ToneMappedFromHdr,
    [property: JsonPropertyName("ocrText")] string? OcrText)
{
    public const string PngKeyword = "Capta";

    /// <summary>
    /// Builds the metadata for a capture. Its text is found with on-device OCR, unless "Save text found in captures" is off
    /// (Settings.SaveTextInCaptures), in which case there is no OCR and no <c>ocrText</c> in the file.
    /// </summary>
    public static async Task<CaptureMetadata> ForAsync(CaptureResult result)
    {
        var text = await CaptureText.ForMetadataAsync(Settings.SaveTextInCaptures, () => Ocr.RecognizeAsync(result.Image));

        return new CaptureMetadata(
            Version: 1,
            Software: "Capta",
            Mode: result.Mode.ToString(),
            CapturedAt: result.CapturedAt,
            SourceApp: result.SourceApp,
            WindowTitle: result.WindowTitle,
            Width: result.Image.Width,
            Height: result.Image.Height,
            ToneMappedFromHdr: result.WasHdr,
            OcrText: text);
    }

    public string ToJson() => JsonSerializer.Serialize(this, CaptaJson.Default.CaptureMetadata); // default encoder escapes non-ASCII, as tEXt (Latin-1) needs
}
