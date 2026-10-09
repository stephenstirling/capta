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

/// <summary>The keyboard shortcut for each action, persisted in Settings (DESIGN.md Decisions 1 are the defaults).</summary>
public static class Shortcuts
{
    public static readonly IReadOnlyDictionary<HotkeyAction, Chord> Defaults = new Dictionary<HotkeyAction, Chord>
    {
        [HotkeyAction.Region] = new(false, false, false, Chord.PrintScreen),
        [HotkeyAction.FullScreen] = new(false, true, false, Chord.PrintScreen),
        [HotkeyAction.ActiveWindow] = new(false, false, true, Chord.PrintScreen),
        [HotkeyAction.Record] = new(true, false, false, Chord.PrintScreen),
        [HotkeyAction.GrabText] = new(true, true, false, Chord.PrintScreen),
    };

    private static readonly Dictionary<HotkeyAction, Chord> s_current = new(Defaults);

    /// <summary>Raised on the UI thread when any shortcut changes.</summary>
    public static event Action? Changed;

    public static Chord For(HotkeyAction action) => s_current[action];

    public static HotkeyAction? ActionFor(Chord chord)
    {
        foreach (var (action, bound) in s_current)
            if (bound == chord) return action;
        return null;
    }

    public static void Load()
    {
        foreach (var action in Defaults.Keys)
            s_current[action] = Chord.Parse(Settings.GetShortcut(action)) ?? Defaults[action];
    }

    /// <summary>
    /// Binds <paramref name="chord"/> to <paramref name="action"/>. If another action had it,
    /// the two swap. Returns the action that was swapped, if any.
    /// </summary>
    public static HotkeyAction? Set(HotkeyAction action, Chord chord)
    {
        var other = ActionFor(chord);
        if (other == action) return null;
        if (other is { } swapped)
        {
            s_current[swapped] = s_current[action];
            Settings.SetShortcut(swapped, s_current[swapped].Serialize());
        }
        s_current[action] = chord;
        Settings.SetShortcut(action, chord.Serialize());
        Changed?.Invoke();
        return other;
    }

    public static void ResetAll()
    {
        foreach (var (action, chord) in Defaults)
        {
            s_current[action] = chord;
            Settings.SetShortcut(action, null);
        }
        Changed?.Invoke();
    }
}
