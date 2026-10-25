using System.Text;
using static Capta.Interop.NativeMethods;

namespace Capta.Services;

/// <summary>A key plus modifiers, e.g. Ctrl + Shift + PrtSc.</summary>
public readonly record struct Chord(bool Ctrl, bool Shift, bool Alt, int Key)
{
    public const int PrintScreen = VK_SNAPSHOT;

    /// <summary>
    /// Chords that would swallow ordinary typing aren't allowed: a chord needs Ctrl or Alt, or a key
    /// that types nothing: Print Screen, Insert, Pause, Scroll Lock or F13 to F24. (Win chords stay
    /// with Windows; the hook never sees them as Capta's.) Binding Insert on its own stops it
    /// toggling overwrite mode in other apps, which is why it has to be chosen, not a default.
    /// </summary>
    public bool IsAllowed => Ctrl || Alt || IsTypingFree(Key);

    private const int Insert = 0x2D, Pause = 0x13, ScrollLock = 0x91, F13 = 0x7C, F24 = 0x87;

    private static bool IsTypingFree(int key) => key is PrintScreen or Insert or Pause or ScrollLock or (>= F13 and <= F24);

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Ctrl) sb.Append("Ctrl + ");
        if (Shift) sb.Append("Shift + ");
        if (Alt) sb.Append("Alt + ");
        sb.Append(KeyName(Key));
        return sb.ToString();
    }

    /// <summary>Compact form for menus and tooltips: "Ctrl+Shift+PrtSc".</summary>
    public string Compact => ToString().Replace(" + ", "+");

    public string Serialize() => $"{(Ctrl ? "C" : "")}{(Shift ? "S" : "")}{(Alt ? "A" : "")}:{Key}";

    public static Chord? Parse(string? text)
    {
        if (text is null) return null;
        var parts = text.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var key)) return null;
        return new Chord(parts[0].Contains('C'), parts[0].Contains('S'), parts[0].Contains('A'), key);
    }

    private static string KeyName(int vk) => vk switch
    {
        PrintScreen => "PrtSc",
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        0x20 => "Space",
        0x2D => "Insert",
        0x2E => "Delete",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "PgUp",
        0x22 => "PgDn",
        0x13 => "Pause",
        0x91 => "ScrLk",
        _ => ((Windows.System.VirtualKey)vk).ToString(),
    };
}
