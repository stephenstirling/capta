using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using static Capta.Interop.NativeMethods;

namespace Capta.Services;

/// <summary>
/// Low-level keyboard hook that claims Print Screen and its Shift/Ctrl/Alt chords.
/// Win+PrtSc is left alone so the Windows "save screenshot" shortcut keeps working.
/// </summary>
/// <remarks>
/// The hook runs on the UI thread (it needs a message loop) and must return quickly,
/// so it only classifies the chord and posts the action back to the dispatcher.
/// </remarks>
public sealed class PrintScreenHook : IDisposable
{
    private static PrintScreenHook? s_instance;

    private readonly DispatcherQueue _dispatcher;
    private nint _hook;
    private bool _swallowingKeyUp;

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
        if (info.vkCode != VK_SNAPSHOT || (info.flags & LLKHF_INJECTED) != 0)
            return false;

        if (message is WM_KEYUP or WM_SYSKEYUP)
        {
            var swallow = _swallowingKeyUp;
            _swallowingKeyUp = false;
            return swallow;
        }

        if (message is not (WM_KEYDOWN or WM_SYSKEYDOWN))
            return false;

        if (IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN))
            return false;

        // Auto-repeat: keep swallowing, but only act on the first press.
        if (_swallowingKeyUp)
            return true;

        var action = IsKeyDown(VK_MENU) ? HotkeyAction.ShowToolbar
            : IsKeyDown(VK_CONTROL) ? HotkeyAction.FullScreen
            : IsKeyDown(VK_SHIFT) ? HotkeyAction.Window
            : HotkeyAction.Region;

        _swallowingKeyUp = true;
        _dispatcher.TryEnqueue(() => Pressed?.Invoke(action));
        return true;
    }

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
