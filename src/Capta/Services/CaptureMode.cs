namespace Capta.Services;

public enum CaptureMode
{
    Region,
    Window,
    FullScreen,
}

/// <summary>What a Print Screen chord asks Capta to do.</summary>
public enum HotkeyAction
{
    Region,       // PrtSc
    Window,       // Shift+PrtSc
    FullScreen,   // Ctrl+PrtSc
    ShowToolbar,  // Alt+PrtSc
}
