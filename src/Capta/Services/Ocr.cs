using Capta.Capture;
using Windows.Graphics;
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

    /// <summary>Images up to this many pixels are read at twice their size (screen text at 100% is small).</summary>
    private const int UpscaleBelowPixels = 8_000_000;

    /// <summary>
    /// Every recognised word with where it is in the image (pixels), line by line. Images bigger than
    /// the engine's limit are read in equal tiles; a word on a tile's edge may be split.
    /// </summary>
    /// <remarks>
    /// Text is read at 2x: at 100% display scaling, Windows OCR splits or drops words in 12-14 px screen
    /// text (an email address read as "jane.doe@example." and "com"). Positions are mapped back.
    /// </remarks>
    public static async Task<IReadOnlyList<RecognizedLine>> RecognizeWordsAsync(CapturedImage original)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("No OCR language is installed. Add one in Settings > Time & language.");
        var scale = (long)original.Width * original.Height <= UpscaleBelowPixels ? 2 : 1;
        var image = scale == 2 ? await Task.Run(original.Upscale2x) : original;
        var max = (int)OcrEngine.MaxImageDimension;
        int columns = (image.Width + max - 1) / max, rows = (image.Height + max - 1) / max;
        int tileWidth = (image.Width + columns - 1) / columns, tileHeight = (image.Height + rows - 1) / rows;

        var lines = new List<RecognizedLine>();
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var (x, y) = (column * tileWidth, row * tileHeight);
            var tile = columns * rows == 1 ? image : image.Crop(new RectInt32(x, y, tileWidth, tileHeight));
            using var bitmap = tile.ToSoftwareBitmap();
            var result = await engine.RecognizeAsync(bitmap);
            foreach (var line in result.Lines)
                lines.Add(new RecognizedLine(line.Words
                    .Select(w => new RecognizedWord(w.Text,
                        (x + w.BoundingRect.X) / scale, (y + w.BoundingRect.Y) / scale,
                        w.BoundingRect.Width / scale, w.BoundingRect.Height / scale))
                    .ToList()));
        }
        return lines;
    }
}
