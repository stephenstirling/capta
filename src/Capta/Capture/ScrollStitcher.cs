namespace Capta.Capture;

/// <summary>
/// Joins frames of a region captured while its content scrolls down into one tall image.
/// </summary>
/// <remarks>
/// Each frame is matched against the previous one by comparing row hashes. Rows that don't change
/// between the first two frames at the top and bottom (sticky headers, footers) are kept once,
/// not repeated. The rightmost columns are left out of the hashes because a scrollbar thumb moves
/// on every step.
/// </remarks>
public sealed class ScrollStitcher
{
    private const int IgnoreRightColumns = 24;
    // Fraction of overlapping rows that must agree. Not 1: apps redraw details lazily between
    // frames (spell-check underlines, blinking carets, hover effects).
    private const double MinMatch = 0.7;
    private const double MinOverlap = 0.2;  // of the scrolling band

    private readonly int _width;
    private readonly int _height;
    private readonly List<byte[]> _rows = []; // stitched rows (header + scrolled content), 4 bytes per pixel
    private CapturedImage _previous;
    private ulong[] _previousHashes;
    private int _top = -1;    // static rows at the top (header), set by the first match
    private int _bottom = -1; // static rows at the bottom (footer)

    public ScrollStitcher(CapturedImage first)
    {
        _width = first.Width;
        _height = first.Height;
        _previous = first;
        _previousHashes = RowHashes(first);
    }

    public int Height => _rows.Count == 0 ? _height : _rows.Count + Math.Max(0, _bottom);

    /// <summary>
    /// Adds the next frame. Returns how many new rows of content it contributed: 0 means the
    /// content didn't move (the end was reached), -1 that it couldn't be matched.
    /// </summary>
    public int Add(CapturedImage frame)
    {
        if (frame.Width != _width || frame.Height != _height) return -1;
        var hashes = RowHashes(frame);

        int top = _top, bottom = _bottom;
        if (top < 0)
        {
            top = 0;
            while (top < _height && hashes[top] == _previousHashes[top]) top++;
            if (top == _height) return 0; // nothing moved at all
            bottom = 0;
            while (bottom < _height - top && hashes[_height - 1 - bottom] == _previousHashes[_height - 1 - bottom]) bottom++;
        }

        var band = _height - top - bottom;
        if (band < 8) return -1;
        var shift = FindShift(_previousHashes, hashes, top, band);
        if (shift < 0) return -1;
        if (shift == 0) return 0;

        if (_top < 0)
        {
            // First match: fix the header/footer and start with the first frame minus its footer.
            _top = top;
            _bottom = bottom;
            for (var y = 0; y < _height - bottom; y++) _rows.Add(Row(_previous, y));
        }

        // The last `shift` rows of the band are new.
        for (var y = top + band - shift; y < top + band; y++) _rows.Add(Row(frame, y));
        _previous = frame;
        _previousHashes = hashes;
        return shift;
    }

    /// <summary>The stitched image, with the footer from the last frame.</summary>
    public CapturedImage Build()
    {
        if (_rows.Count == 0) return _previous;
        var footer = Math.Max(0, _bottom);
        var height = _rows.Count + footer;
        var pixels = new byte[_width * height * 4];
        for (var y = 0; y < _rows.Count; y++)
            Buffer.BlockCopy(_rows[y], 0, pixels, y * _width * 4, _width * 4);
        for (var y = 0; y < footer; y++)
            Buffer.BlockCopy(_previous.Pixels, (_height - footer + y) * _width * 4, pixels, (_rows.Count + y) * _width * 4, _width * 4);
        return new CapturedImage(_width, height, pixels);
    }

    /// <summary>
    /// The scroll distance: the smallest shift d where the band of <paramref name="cur"/> starting
    /// at row 0 matches the band of <paramref name="prev"/> starting at row d. -1 if none fits.
    /// </summary>
    private static int FindShift(ulong[] prev, ulong[] cur, int top, int band)
    {
        var minOverlap = Math.Max(8, (int)(band * MinOverlap));
        var best = -1;
        var bestScore = 0.0;
        for (var d = 0; d <= band - minOverlap; d++)
        {
            var overlap = band - d;
            var same = 0;
            for (var i = 0; i < overlap; i++)
                if (cur[top + i] == prev[top + d + i]) same++;
            var score = same / (double)overlap;
            // The best-matching shift wins; on a near tie (uniform rows match anywhere) the
            // smaller one, which is the more likely scroll distance.
            if (score >= MinMatch && score > bestScore + 0.01)
            {
                best = d;
                bestScore = score;
                if (score == 1.0) break;
            }
        }
        return best;
    }

    private ulong[] RowHashes(CapturedImage image)
    {
        var columns = Math.Max(1, _width - IgnoreRightColumns);
        var hashes = new ulong[_height];
        for (var y = 0; y < _height; y++)
        {
            var span = image.Pixels.AsSpan(y * _width * 4, columns * 4);
            ulong h = 14695981039346656037;
            foreach (var b in span) h = (h ^ b) * 1099511628211;
            hashes[y] = h;
        }
        return hashes;
    }

    private byte[] Row(CapturedImage image, int y) => image.Pixels.AsSpan(y * _width * 4, _width * 4).ToArray();
}
