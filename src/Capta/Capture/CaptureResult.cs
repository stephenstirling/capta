using Capta.Services;
using Windows.Graphics;

namespace Capta.Capture;

/// <param name="Image">The SDR capture.</param>
/// <param name="Monitor">Monitor the capture came from (where the card appears).</param>
/// <param name="ScreenBounds">Where the captured pixels were on the virtual desktop (where a pin appears).</param>
/// <param name="Mode">How it was captured.</param>
/// <param name="WasHdr">The source display was in HDR, so the image was tone-mapped.</param>
public sealed record CaptureResult(CapturedImage Image, MonitorInfo Monitor, RectInt32 ScreenBounds, CaptureMode Mode, bool WasHdr)
{
    /// <summary>
    /// The SDR capture. Settable because redacting personal info (or undoing it) replaces the image
    /// the card, clipboard, saves and pins use.
    /// </summary>
    public CapturedImage Image { get; set; } = Image;

    /// <summary>Personal info found in the capture and what's redacted; null when none was found or looked for.</summary>
    public Redaction? Redaction { get; set; }

    /// <summary>App the captured window belongs to (process name), when known.</summary>
    public string? SourceApp { get; init; }

    public string? WindowTitle { get; init; }

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>For recordings: the MP4 (Image is a frame from it, for thumbnails).</summary>
    public string? VideoPath { get; init; }

    public TimeSpan VideoLength { get; init; }

    public bool IsVideo => VideoPath is not null;

    /// <summary>Set once the capture has been saved; enables "Copy file path".</summary>
    public string? SavedPath { get; set; }

    public string ModeName => Mode switch
    {
        CaptureMode.Region => "Region",
        CaptureMode.Window => "Window",
        CaptureMode.FullScreen => "Full screen",
        CaptureMode.ActiveWindow => "Active window",
        CaptureMode.GrabText => "Grab text",
        CaptureMode.Freeform => "Freeform",
        CaptureMode.Scrolling => "Scrolling",
        CaptureMode.Recording => "Recording",
        _ => Mode.ToString(),
    };
}
