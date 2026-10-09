namespace Capta.Services;

public enum CaptureMode
{
    Region,
    Window,       // pick a window on the overlay
    FullScreen,
    ActiveWindow, // the foreground window, no overlay
    GrabText,     // select a region on the overlay; its text is copied
    Freeform,     // draw around any shape on the overlay; outside it is transparent
    Scrolling,    // select a region; Capta scrolls it and stitches one tall image
    Recording,    // select a region; Capta records it to MP4
}

/// <summary>
/// What a Print Screen chord asks Capta to do (design/DESIGN.md, Decisions 1; these override
/// Startup.html). Win+PrtSc is left to Windows.
/// </summary>
public enum HotkeyAction
{
    Region,       // PrtSc
    FullScreen,   // Shift+PrtSc: full screen, straight to the clipboard
    ActiveWindow, // Alt+PrtSc
    Record,       // Ctrl+PrtSc: start recording video; again to stop
    GrabText,     // Ctrl+Shift+PrtSc
}
