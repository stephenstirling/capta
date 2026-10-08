namespace Capta.Services;

public enum CaptureMode
{
    Region,
    Window,       // pick a window on the overlay
    FullScreen,
    ActiveWindow, // the foreground window, no overlay
}

/// <summary>
/// What a Print Screen chord asks Capta to do (design/DESIGN.md, Decisions 1; these override
/// Startup.html). Win+PrtSc is left to Windows. Ctrl+Shift+PrtSc will be Grab text; until then
/// it counts as Ctrl+PrtSc.
/// </summary>
public enum HotkeyAction
{
    Region,       // PrtSc
    FullScreen,   // Shift+PrtSc: full screen, straight to the clipboard
    ActiveWindow, // Alt+PrtSc
    ShowToolbar,  // Ctrl+PrtSc: for now; becomes Record video
}
