using Capta.Capture;

namespace Capta.Services;

/// <summary>
/// The personal info found in a capture and which of it is blurred. Keeps the original capture so
/// each area can be turned off again (Review, Undo) without re-capturing.
/// </summary>
public sealed class Redaction
{
    private readonly bool[] _blurred;

    public Redaction(CapturedImage original, IReadOnlyList<PersonalInfoMatch> matches)
    {
        Original = original;
        Matches = matches;
        _blurred = new bool[matches.Count];
        Array.Fill(_blurred, true);
    }

    /// <summary>The capture before anything was blurred.</summary>
    public CapturedImage Original { get; }

    public IReadOnlyList<PersonalInfoMatch> Matches { get; }

    public bool IsBlurred(int index) => _blurred[index];

    public void Set(int index, bool blurred) => _blurred[index] = blurred;

    public void SetAll(bool blurred) => Array.Fill(_blurred, blurred);

    public int BlurredCount => _blurred.Count(b => b);

    /// <summary>The capture with the blurred areas applied (the original itself when none are).</summary>
    public CapturedImage Render() =>
        BlurredCount == 0
            ? Original
            : Redactor.Blur(Original, Matches.Where((_, i) => _blurred[i]).Select(m => m.Bounds));
}
