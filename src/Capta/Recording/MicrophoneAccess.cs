using Capta.Services;
using Windows.Media.Capture;

namespace Capta.Recording;

/// <summary>
/// Asks Windows for microphone access (the consent prompt appears the first time). WASAPI alone
/// doesn't show the prompt for packaged apps; initialising an audio-only MediaCapture does.
/// </summary>
public static class MicrophoneAccess
{
    public static async Task<bool> RequestAsync()
    {
        try
        {
            using var capture = new MediaCapture();
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Audio,
                MediaCategory = MediaCategory.Other,
            });
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            Log.Info("Microphone access denied");
            return false;
        }
        catch (Exception ex)
        {
            Log.Info($"No microphone: {ex.Message}");
            return false;
        }
    }
}
