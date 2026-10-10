namespace Capta.Services;

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
