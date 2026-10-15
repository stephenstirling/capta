using Capta.Services;

namespace Capta.Tests;

public class PersonalInfoTests
{
    /// <summary>A line of recognised words: 8 px per character, 16 px high, starting at x = 10.</summary>
    private static RecognizedLine Line(string text, double y = 20)
    {
        var words = new List<RecognizedWord>();
        double x = 10;
        foreach (var word in text.Split(' '))
        {
            words.Add(new RecognizedWord(word, x, y, word.Length * 8, 16));
            x += word.Length * 8 + 8;
        }
        return new RecognizedLine(words);
    }

    private static IReadOnlyList<PersonalInfoMatch> Find(params string[] lines) =>
        PersonalInfo.Find(lines.Select(l => Line(l)));

    [Fact]
    public void FindsAnEmailAddress()
    {
        var match = Assert.Single(Find("Contact me@example.com today"));
        Assert.Equal(PersonalInfoKind.Email, match.Kind);
        Assert.Equal("me@example.com", match.Text);
    }

    [Fact]
    public void AnEmailInPunctuationIsFoundWithoutIt()
    {
        var match = Assert.Single(Find("Mail (jo.smith+tag@mail.example.co.uk), please"));
        Assert.Equal("jo.smith+tag@mail.example.co.uk", match.Text);
    }

    [Theory]
    [InlineData("(555) 123-4567")]
    [InlineData("555-123-4567")]
    [InlineData("555.123.4567")]
    [InlineData("555 123 4567")]
    [InlineData("+1 555 123 4567")]
    [InlineData("1-800-555-0199")]
    public void FindsPhoneNumbers(string number)
    {
        var match = Assert.Single(Find($"Call {number} now"));
        Assert.Equal(PersonalInfoKind.Phone, match.Kind);
        Assert.Equal(number, match.Text);
    }

    [Theory]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("1234567890123")]       // 13 digits is the shortest long run
    [InlineData("1234567890123456789")]
    public void FindsLongDigitRuns(string number)
    {
        var match = Assert.Single(Find($"Card {number} exp"));
        Assert.Equal(PersonalInfoKind.Number, match.Kind);
        Assert.Equal(number, match.Text);
    }

    [Fact]
    public void TwelveDigitsIsNotALongRun()
    {
        // 10 to 12 bare digits read as a phone number; 13 and up are the long-run rule.
        var match = Assert.Single(Find("Ref 123456789012 ok"));
        Assert.Equal(PersonalInfoKind.Phone, match.Kind);
    }

    [Theory]
    [InlineData("Mail me@example.com.", "me@example.com")]
    [InlineData("Mail <ME.Smith@Example.COM> now", "ME.Smith@Example.COM")]
    [InlineData("a_b-c+tag@sub.domain.example.org", "a_b-c+tag@sub.domain.example.org")]
    public void FindsEmailVariants(string text, string expected) =>
        Assert.Equal(expected, Assert.Single(Find(text)).Text);

    [Theory]
    [InlineData("Follow @mention on social")]
    [InlineData("user@ is incomplete")]
    [InlineData("see example.com for more")]
    [InlineData("a@b is not an address")]
    public void IgnoresTextThatIsNotAnEmail(string text) => Assert.Empty(Find(text));

    [Theory]
    [InlineData("+44 20 7946 0958")]   // international (12 digits, grouped: found as a long number is not required)
    [InlineData("(020) 7946 0958")]
    [InlineData("020.7946.0958")]
    public void FindsInternationalPhoneFormats(string number)
    {
        var match = Assert.Single(Find($"Tel {number} now"));
        Assert.Contains(match.Kind, new[] { PersonalInfoKind.Phone, PersonalInfoKind.Number });
        Assert.Equal(number.TrimStart('+'), match.Text.TrimStart('+'));
    }

    [Theory]
    [InlineData("Updated 2026-10-15")]
    [InlineData("Server 192.168.100.100 is up")]
    [InlineData("Build 10.0.26100.5000")]
    [InlineData("Total $1,234,567.89")]
    [InlineData("Version 3.2.1")]
    [InlineData("Time 12:30:45")]
    [InlineData("Order 1234567")]
    [InlineData("Page 42 of 300")]
    [InlineData("name at example dot com")]
    public void IgnoresThingsThatOnlyLookLikeNumbersOrEmails(string text) =>
        Assert.Empty(Find(text));

    [Fact]
    public void ACardNumberIsNotAlsoAPhoneNumber() =>
        Assert.Equal(PersonalInfoKind.Number, Assert.Single(Find("4111 1111 1111 1111")).Kind);

    [Fact]
    public void TheBoundsCoverTheWordsPlusPadding()
    {
        // "me@example.com" is one word: x 10 + 8 per character, 16 px high, at y = 20.
        var line = new RecognizedLine([new RecognizedWord("me@example.com", 10, 20, 112, 16)]);
        var bounds = Assert.Single(PersonalInfo.Find([line])).Bounds;
        Assert.Equal((7, 17, 118, 22), (bounds.X, bounds.Y, bounds.Width, bounds.Height));
    }

    [Fact]
    public void ANumberSplitAcrossWordsCoversAllOfThem()
    {
        // "(555)" at x = 10 (5 chars, 40 px) and "123-4567" at x = 58 (8 chars, 64 px).
        var match = Assert.Single(Find("(555) 123-4567"));
        Assert.Equal(7, match.Bounds.X);
        Assert.Equal(58 + 64 + 3, match.Bounds.X + match.Bounds.Width);
    }

    [Fact]
    public void OnlyTheWordsAMatchTouchesAreCovered()
    {
        var match = Assert.Single(Find("Write to me@example.com now"));
        // "Write" (10..50), "to" (58..74), the address starts at x = 82.
        Assert.Equal(79, match.Bounds.X);
    }

    [Fact]
    public void MatchesComeBackInReadingOrder()
    {
        var lines = new[] { Line("call 555-123-4567", y: 20), Line("or mail a@b.org", y: 60) };
        var matches = PersonalInfo.Find(lines);
        Assert.Equal(new[] { PersonalInfoKind.Phone, PersonalInfoKind.Email }, matches.Select(m => m.Kind));
    }

    [Fact]
    public void DescribesWhatWasFound()
    {
        var matches = Find("a@b.org", "555-123-4567", "(555) 987-6543", "4111 1111 1111 1111");
        Assert.Equal("1 email address, 2 phone numbers, 1 long number", PersonalInfo.Describe(matches));
        Assert.Equal("2 email addresses", PersonalInfo.Describe(Find("a@b.org", "c@d.org")));
    }
}
