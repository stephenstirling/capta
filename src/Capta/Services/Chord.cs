using System.Text;
using static Capta.Interop.NativeMethods;

namespace Capta.Services;

/// <summary>A key plus modifiers, e.g. Ctrl + Shift + PrtSc.</summary>
public readonly record struct Chord(bool Ctrl, bool Shift, bool Alt, int Key)
{
    public const int PrintScreen = VK_SNAPSHOT;

    /// <summary>
    /// Chords that would swallow ordinary typing aren't allowed: a chord needs Print Screen or
    /// Ctrl/Alt. (Win chords stay with Windows; the hook never sees them as Capta's.)
    /// </summary>
    public bool IsAllowed => Key == PrintScreen || Ctrl || Alt;

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
