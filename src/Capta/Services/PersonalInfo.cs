using System.Text;
using System.Text.RegularExpressions;
using Windows.Graphics;

namespace Capta.Services;

public enum PersonalInfoKind
{
    Email,
    Phone,
    Number,
}

/// <summary>One word of recognised text, in image pixels.</summary>
public readonly record struct RecognizedWord(string Text, double X, double Y, double Width, double Height);

/// <summary>One line of recognised text.</summary>
public sealed record RecognizedLine(IReadOnlyList<RecognizedWord> Words);

/// <param name="Text">What was found, as recognised.</param>
/// <param name="Bounds">The words it covers, in image pixels (a little larger than the text).</param>
public sealed record PersonalInfoMatch(PersonalInfoKind Kind, string Text, RectInt32 Bounds)
{
    public string Label => Kind switch
    {
        PersonalInfoKind.Email => "Email address",
        PersonalInfoKind.Phone => "Phone number",
        _ => "Long number",
    };
}

/// <summary>
/// Finds email addresses, phone numbers and long digit runs (13 or more digits: card, account and ID
/// numbers) in recognised text, so they can be covered before a capture leaves Capta. It only looks at the
/// text OCR found, so it can miss what OCR misreads, and it will cover some numbers that aren't
/// personal: that's why the card offers Review and Undo.
/// </summary>
public static partial class PersonalInfo
{
    /// <summary>Space around the words, so the fill covers the glyphs' edges.</summary>
    private const int Padding = 3;

    private const int MinPhoneDigits = 10;
    private const int MaxPhoneDigits = 15;

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}")]
    private static partial Regex EmailPattern();

    // 13 or more digits, which may be grouped with single spaces or hyphens (4111 1111 1111 1111): card-like
    // numbers. A bare run of 10 to 12 digits is read as a phone number instead.
    [GeneratedRegex(@"(?<!\d)\d(?:[ \-]?\d){12,}(?!\d)")]
    private static partial Regex LongNumberPattern();

    // 555-123-4567, (555) 123-4567, +1 555 123 4567, +44 20 7946 0958. The digit count and a few
    // look-alikes (IP addresses, dates) are checked afterwards.
    [GeneratedRegex(@"(?<![\w.\-])(?:\+?\d{1,3}[\s.\-]?)?(?:\(\d{2,4}\)|\d{2,4})[\s.\-]?\d{3,4}[\s.\-]?\d{3,4}(?!\w)")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^\d{1,3}(?:\.\d{1,3}){3}$")]
    private static partial Regex Ipv4Pattern();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}")]
    private static partial Regex IsoDatePattern();

    /// <summary>The personal info in the lines, in reading order.</summary>
    public static IReadOnlyList<PersonalInfoMatch> Find(IEnumerable<RecognizedLine> lines)
    {
        var found = new List<PersonalInfoMatch>();
        foreach (var line in lines)
            FindInLine(line, found);
        return found;
    }

    private static void FindInLine(RecognizedLine line, List<PersonalInfoMatch> found)
    {
        // The line as one string, with where each word sits in it.
        var text = new StringBuilder();
        var words = new List<(RecognizedWord Word, int Start, int End)>();
        foreach (var word in line.Words)
        {
            if (string.IsNullOrWhiteSpace(word.Text)) continue;
            if (text.Length > 0) text.Append(' ');
            var start = text.Length;
            text.Append(word.Text.Trim());
            words.Add((word, start, text.Length));
        }
        if (words.Count == 0) return;
        var s = text.ToString();

        // Earlier kinds win where matches overlap: a card number is also phone-shaped.
        var taken = new List<(int Start, int End, PersonalInfoKind Kind)>();
        void Take(PersonalInfoKind kind, IEnumerable<Match> matches, Func<string, bool>? accept = null)
        {
            foreach (var m in matches)
            {
                var value = m.Value.Trim();
                if (accept is not null && !accept(value)) continue;
                var (start, end) = (m.Index, m.Index + m.Length);
                if (taken.Any(t => start < t.End && end > t.Start)) continue;
                taken.Add((start, end, kind));
            }
        }
        Take(PersonalInfoKind.Email, EmailPattern().Matches(s));
        Take(PersonalInfoKind.Number, LongNumberPattern().Matches(s));
        Take(PersonalInfoKind.Phone, PhonePattern().Matches(s), IsPhone);

        foreach (var (start, end, kind) in taken.OrderBy(t => t.Start))
        {
            var covered = words.Where(w => w.Start < end && w.End > start).Select(w => w.Word).ToList();
            if (covered.Count == 0) continue;
            found.Add(new PersonalInfoMatch(kind, s[start..end].Trim(), Union(covered)));
        }
    }

    private static bool IsPhone(string value)
    {
        var digits = value.Count(char.IsDigit);
        return digits is >= MinPhoneDigits and <= MaxPhoneDigits
            && !Ipv4Pattern().IsMatch(value)
            && !IsoDatePattern().IsMatch(value);
    }

    private static RectInt32 Union(List<RecognizedWord> words)
    {
        var x0 = words.Min(w => w.X);
        var y0 = words.Min(w => w.Y);
        var x1 = words.Max(w => w.X + w.Width);
        var y1 = words.Max(w => w.Y + w.Height);
        var left = (int)Math.Floor(x0) - Padding;
        var top = (int)Math.Floor(y0) - Padding;
        var right = (int)Math.Ceiling(x1) + Padding;
        var bottom = (int)Math.Ceiling(y1) + Padding;
        return new RectInt32(left, top, right - left, bottom - top);
    }

    /// <summary>"1 email address", "2 phone numbers, 1 long number".</summary>
    public static string Describe(IReadOnlyList<PersonalInfoMatch> matches)
    {
        var parts = new List<string>();
        foreach (var kind in new[] { PersonalInfoKind.Email, PersonalInfoKind.Phone, PersonalInfoKind.Number })
        {
            var n = matches.Count(m => m.Kind == kind);
            if (n == 0) continue;
            parts.Add(kind switch
            {
                PersonalInfoKind.Email => n == 1 ? "1 email address" : $"{n} email addresses",
                PersonalInfoKind.Phone => n == 1 ? "1 phone number" : $"{n} phone numbers",
                _ => n == 1 ? "1 long number" : $"{n} long numbers",
            });
        }
        return string.Join(", ", parts);
    }
}
