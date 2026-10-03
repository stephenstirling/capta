using Microsoft.Win32;
using Windows.System;
using static Capta.Interop.NativeMethods;

namespace Capta.Services;

/// <summary>
/// Tracks whether Windows has bound Print Screen to the Snipping Tool
/// (Settings > Accessibility > Keyboard > "Use the Print screen key to open screen capture").
/// While that's on, Windows competes with Capta for the key.
/// </summary>
public sealed class PrintScreenOwnership : IDisposable
{
    private const string KeyPath = @"Control Panel\Keyboard";
    private const string ValueName = "PrintScreenKeyForSnippingEnabled";
    public static readonly Uri SettingsUri = new("ms-settings:easeofaccess-keyboard");

    private readonly RegistryKey? _key;
    private readonly AutoResetEvent _changed = new(false);
    private RegisteredWaitHandle? _wait;

    /// <summary>Raised on a thread-pool thread when the setting flips.</summary>
    public event Action<bool>? Changed;

    public bool WindowsOwnsKey { get; private set; }

    public PrintScreenOwnership()
    {
        _key = Registry.CurrentUser.OpenSubKey(KeyPath);
        WindowsOwnsKey = Read();
    }

    /// <remarks>Windows 11 defaults the setting to on, so a missing value means Windows owns the key.</remarks>
    private bool Read() => _key?.GetValue(ValueName) is not int v || v != 0;

    public void StartWatching()
    {
        if (_key is null || _wait is not null) return;
        Arm();
        _wait = ThreadPool.RegisterWaitForSingleObject(_changed, (_, _) =>
        {
            Arm();
            var now = Read();
            if (now == WindowsOwnsKey) return;
            WindowsOwnsKey = now;
            Changed?.Invoke(now);
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>RegNotifyChangeKeyValue is one-shot, so re-arm after every signal.</summary>
    private void Arm() =>
        RegNotifyChangeKeyValue(_key!.Handle.DangerousGetHandle(), false, REG_NOTIFY_CHANGE_LAST_SET,
            _changed.SafeWaitHandle.DangerousGetHandle(), true);

    public static Task OpenSettingsAsync() => Launcher.LaunchUriAsync(SettingsUri).AsTask();

    public void Dispose()
    {
        _wait?.Unregister(null);
        _key?.Dispose();
        _changed.Dispose();
    }
}
