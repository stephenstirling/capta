using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using static Capta.Interop.NativeMethods;

namespace Capta.Services;

/// <summary>
/// Low-level keyboard hook for Capta's shortcuts (Print Screen chords by default; see
/// <see cref="Shortcuts"/>). Chords with the Win key are left alone, so Windows keeps
/// Win+PrtSc and Win+Shift+S.
/// </summary>
/// <remarks>
/// The hook runs on the UI thread (it needs a message loop) and must return quickly, so it
/// only matches the chord and posts the result back to the dispatcher.
/// </remarks>
public sealed class PrintScreenHook : IDisposable
{
    private static PrintScreenHook? s_instance;

    private readonly DispatcherQueue _dispatcher;
    private nint _hook;
    private int _swallowingKeyUp; // vk whose key-up we swallow, or 0
    private Action<Chord?>? _recorder;

    public event Action<HotkeyAction>? Pressed;

    public PrintScreenHook(DispatcherQueue dispatcher) => _dispatcher = dispatcher;

    public unsafe void Install()
    {
        if (_hook != 0) return;
        s_instance = this;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, &HookProc, GetModuleHandle(null), 0);
        if (_hook == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
    }

    /// <summary>
    /// Captures the next chord instead of acting on it (for changing a shortcut). The callback
    /// gets the chord, or null if Esc was pressed. Recording ends after one chord.
    /// </summary>
    public void Record(Action<Chord?> callback) => _recorder = callback;

    public void CancelRecording() => _recorder = null;

    [UnmanagedCallersOnly]
    private static nint HookProc(int nCode, nint wParam, nint lParam)
    {
        var self = s_instance;
        if (nCode == HC_ACTION && self is not null && self.Handle((int)wParam, lParam))
            return 1; // swallow
        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    private unsafe bool Handle(int message, nint lParam)
    {
        ref var info = ref *(KBDLLHOOKSTRUCT*)lParam;
        var vk = (int)info.vkCode;

        // Key-ups first, injected or not, so the release of a swallowed key always clears it.
        if (message is WM_KEYUP or WM_SYSKEYUP)
        {
            if (vk != _swallowingKeyUp) return false;
            _swallowingKeyUp = 0;
            return true;
        }

        // Synthetic input never triggers a capture, but it can set a shortcut while recording
        // (e.g. from the on-screen keyboard).
        if ((info.flags & LLKHF_INJECTED) != 0 && _recorder is null)
            return false;

        if (message is not (WM_KEYDOWN or WM_SYSKEYDOWN) || IsModifier(vk))
            return false;
        if (IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN))
            return false;

        // Auto-repeat: keep swallowing, but only act on the first press.
        if (vk == _swallowingKeyUp)
            return true;

        var chord = new Chord(IsKeyDown(VK_CONTROL), IsKeyDown(VK_SHIFT), IsKeyDown(VK_MENU), vk);

        if (_recorder is { } recorder)
        {
            _recorder = null;
            _swallowingKeyUp = vk;
            Chord? recorded = vk == 0x1B && !chord.Ctrl && !chord.Shift && !chord.Alt ? null : chord; // Esc cancels
            _dispatcher.TryEnqueue(() => recorder(recorded));
            return true;
        }

        if (Shortcuts.ActionFor(chord) is not { } action)
            return false;

        _swallowingKeyUp = vk;
        _dispatcher.TryEnqueue(() => Pressed?.Invoke(action));
        return true;
    }

    private static bool IsModifier(int vk) => vk is
        VK_SHIFT or VK_CONTROL or VK_MENU or VK_LWIN or VK_RWIN
        or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5; // left/right Shift, Ctrl, Alt

    public void Dispose()
    {
        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
        if (s_instance == this) s_instance = null;
    }
}
