using Capta.Capture;

namespace Capta.Services;

/// <summary>
/// The personal info found in a capture and which of it is covered. Keeps the original capture so
/// each area can be uncovered again (Review, Undo) without re-capturing.
/// </summary>
public sealed class Redaction
{
    private readonly bool[] _redacted;

    public Redaction(CapturedImage original, IReadOnlyList<PersonalInfoMatch> matches)
    {
        Original = original;
        Matches = matches;
        _redacted = new bool[matches.Count];
        Array.Fill(_redacted, true);
    }

    /// <summary>The capture before anything was covered.</summary>
    public CapturedImage Original { get; }

    public IReadOnlyList<PersonalInfoMatch> Matches { get; }

    public bool IsRedacted(int index) => _redacted[index];

    public void Set(int index, bool redacted) => _redacted[index] = redacted;

    public void SetAll(bool redacted) => Array.Fill(_redacted, redacted);

    public int RedactedCount => _redacted.Count(r => r);

    /// <summary>The capture with the covered areas filled in (the original itself when none are).</summary>
    public CapturedImage Render() =>
        RedactedCount == 0
            ? Original
            : Redactor.Fill(Original, Matches.Where((_, i) => _redacted[i]).Select(m => m.Bounds));
}
