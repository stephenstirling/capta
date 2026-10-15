using Capta.Services;

namespace Capta.Tests;

public class ChordTests
{
    [Theory]
    [InlineData(false, false, false, Chord.PrintScreen, ":44")]
    [InlineData(true, true, false, Chord.PrintScreen, "CS:44")]
    [InlineData(true, false, true, 0x41, "CA:65")]
    public void SerializesAndParsesBack(bool ctrl, bool shift, bool alt, int key, string text)
    {
        var chord = new Chord(ctrl, shift, alt, key);
        Assert.Equal(text, chord.Serialize());
        Assert.Equal(chord, Chord.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CS")]
    [InlineData("C:x")]
    [InlineData("C:1:2")]
    public void UnreadableSettingsParseAsNothing(string? text) => Assert.Null(Chord.Parse(text));

    [Theory]
    [InlineData(true, true, false, Chord.PrintScreen, "Ctrl + Shift + PrtSc", "Ctrl+Shift+PrtSc")]
    [InlineData(false, false, true, 0x70, "Alt + F1", "Alt+F1")]
    [InlineData(true, false, false, 0x39, "Ctrl + 9", "Ctrl+9")]
    [InlineData(true, false, false, 0x22, "Ctrl + PgDn", "Ctrl+PgDn")]
    public void NamesKeys(bool ctrl, bool shift, bool alt, int key, string name, string compact)
    {
        var chord = new Chord(ctrl, shift, alt, key);
        Assert.Equal(name, chord.ToString());
        Assert.Equal(compact, chord.Compact);
    }

    [Theory]
    [InlineData(false, false, false, Chord.PrintScreen, true)]
    [InlineData(false, true, false, Chord.PrintScreen, true)]
    [InlineData(true, false, false, 0x41, true)]
    [InlineData(false, false, true, 0x41, true)]
    [InlineData(false, false, false, 0x41, false)] // plain A would swallow typing
    [InlineData(false, true, false, 0x41, false)]
    public void OnlyChordsThatLeaveTypingAloneAreAllowed(bool ctrl, bool shift, bool alt, int key, bool allowed) =>
        Assert.Equal(allowed, new Chord(ctrl, shift, alt, key).IsAllowed);
}
