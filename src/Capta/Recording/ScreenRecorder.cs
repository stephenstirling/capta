using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Capta.Capture;
using Capta.Services;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Capta.Recording;

/// <summary>
/// Records a monitor region or a window to an H.264/AAC MP4. Frames from Windows.Graphics.Capture
/// (BGRA; Windows converts HDR screens to SDR) are cropped on the GPU and fed, with the mixed
/// system audio and microphone, to a MediaStreamSource that MediaTranscoder encodes.
/// </summary>
public sealed class ScreenRecorder : IAsyncDisposable
{
    private const int FrameRate = 30;
    private static readonly TimeSpan FrameInterval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / FrameRate);
    private static readonly TimeSpan AudioBlock = TimeSpan.FromMilliseconds(10);
    private const int AudioBlockFrames = WasapiSource.SampleRate / 100;

    private readonly Direct3D _d3d;
    private readonly GraphicsCaptureItem _item;
    private readonly RectInt32 _crop;   // in the item's pixels; even width and height
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly Lock _audioLock = new();
    private WasapiSource? _system;
    private WasapiSource? _mic;
    private readonly Stopwatch _clock = new();
    private readonly Lock _frameLock = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly string _path;
    private VideoStreamDescriptor? _videoStream;

    private IDirect3DSurface? _latest;          // newest cropped frame, not yet sent
    private IDirect3DSurface? _lastSent;        // repeated when the screen doesn't change
    private TaskCompletionSource _frameArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TimeSpan _lastFrameTime = TimeSpan.MinValue;
    private long _audioBlocks;
    private Task? _transcode;

    public string Path => _path;
    public int Width => _crop.Width;
    public int Height => _crop.Height;
    public TimeSpan Elapsed => _clock.Elapsed;
    public bool SystemAudio => _system is not null;
    public bool Microphone => _mic is not null;

    /// <summary>
    /// Turns system audio on or off mid-recording (the track is always there; off is silence).
    /// Returns whether it's on: turning it on fails if there's no output device.
    /// </summary>
    public bool SetSystemAudio(bool on) => SetSource(ref _system, on, loopback: true);

    /// <summary>
    /// Turns the microphone on or off. The device is opened only while on, so Windows' "microphone
    /// in use" indicator is accurate. Call <see cref="MicrophoneAccess.RequestAsync"/> first.
    /// </summary>
    public bool SetMicrophone(bool on) => SetSource(ref _mic, on, loopback: false);

    private bool SetSource(ref WasapiSource? source, bool on, bool loopback)
    {
        lock (_audioLock)
        {
            if (on && source is null && !_stop.IsCancellationRequested)
            {
                source = WasapiSource.TryOpen(loopback);
                source?.Start();
            }
            else if (!on && source is not null)
            {
                source.Dispose();
                source = null;
            }
            return source is not null;
        }
    }

    private ScreenRecorder(Direct3D d3d, GraphicsCaptureItem item, RectInt32 crop, string path)
    {
        _d3d = d3d;
        _item = item;
        _crop = crop;
        _path = path;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(d3d.WinRTDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        _pool.FrameArrived += OnFrameArrived;
        _session = _pool.CreateCaptureSession(item);
        _session.IsCursorCaptureEnabled = true;
        try { _session.IsBorderRequired = false; } catch { /* older Windows */ }
        try { _session.MinUpdateInterval = FrameInterval; } catch { /* older Windows; frames are throttled below */ }
    }

    /// <param name="crop">Part of the item to record, in its pixels (the whole item for windows).</param>
    public static async Task<ScreenRecorder> StartAsync(GraphicsCaptureItem item, RectInt32 crop, string path, bool systemAudio, bool microphone)
    {
        // H.264 needs even dimensions.
        crop = new RectInt32(crop.X, crop.Y, Math.Max(2, crop.Width & ~1), Math.Max(2, crop.Height & ~1));
        var recorder = new ScreenRecorder(Direct3D.Create(), item, crop, path);
        recorder.SetSystemAudio(systemAudio);
        recorder.SetMicrophone(microphone);
        await recorder.StartTranscodeAsync();
        return recorder;
    }

    private async Task StartTranscodeAsync()
    {
        var videoProps = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)_crop.Width, (uint)_crop.Height);
        _videoStream = new VideoStreamDescriptor(videoProps);
        // Always an audio track, so sources can be switched on mid-recording.
        var audioProps = AudioEncodingProperties.CreatePcm(WasapiSource.SampleRate, WasapiSource.Channels, 32);
        audioProps.Subtype = MediaEncodingSubtypes.Float;
        var source = new MediaStreamSource(_videoStream, new AudioStreamDescriptor(audioProps));
        source.BufferTime = TimeSpan.Zero;
        source.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
        source.SampleRequested += OnSampleRequested;

        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);
        profile.Video.Width = (uint)_crop.Width;
        profile.Video.Height = (uint)_crop.Height;
        profile.Video.FrameRate.Numerator = FrameRate;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;
        // About 0.1 bits per pixel per frame: crisp text without huge files.
        profile.Video.Bitrate = (uint)Math.Clamp(_crop.Width * _crop.Height * FrameRate / 10, 1_000_000, 40_000_000);
        profile.Audio = AudioEncodingProperties.CreateAac(WasapiSource.SampleRate, WasapiSource.Channels, 192_000);

        var file = await StorageFile.GetFileFromPathAsync(EnsureFile(_path));
        var output = await file.OpenAsync(FileAccessMode.ReadWrite);
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, output, profile);
        if (!prepared.CanTranscode)
            throw new InvalidOperationException($"Can't record video: {prepared.FailureReason}.");

        _session.StartCapture();
        _clock.Start();
        _transcode = RunTranscodeAsync(prepared, output);
    }

    private static async Task RunTranscodeAsync(PrepareTranscodeResult prepared, Windows.Storage.Streams.IRandomAccessStream output)
    {
        try
        {
            await prepared.TranscodeAsync();
        }
        finally
        {
            output.Dispose();
        }
    }

    private static string EnsureFile(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) File.Create(path).Dispose();
        return path;
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool pool, object _)
    {
        using var frame = pool.TryGetNextFrame();
        if (frame is null || _stop.IsCancellationRequested) return;
        var now = _clock.Elapsed;
        if (now - _lastFrameTime < FrameInterval * 0.9) return; // throttle to the frame rate
        _lastFrameTime = now;
        try
        {
            var copy = _d3d.CopyRegion(frame.Surface, _crop.X, _crop.Y, _crop.Width, _crop.Height);
            lock (_frameLock)
            {
                _latest = copy;
                _frameArrived.TrySetResult();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Recording frame copy failed", ex);
        }
    }

    private async void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        var request = args.Request;
        var deferral = request.GetDeferral();
        try
        {
            request.Sample = request.StreamDescriptor is VideoStreamDescriptor ? await NextVideoSampleAsync() : await NextAudioSampleAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Recording sample failed", ex);
            request.Sample = null;
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>The newest frame, or the last one again if the screen hasn't changed; null at the end.</summary>
    private async Task<MediaStreamSample?> NextVideoSampleAsync()
    {
        Task arrived;
        lock (_frameLock) arrived = _frameArrived.Task;
        await Task.WhenAny(arrived, Task.Delay(FrameInterval * 3, _stop.Token).ContinueWith(_ => { }));
        if (_stop.IsCancellationRequested) return null;

        IDirect3DSurface? surface;
        lock (_frameLock)
        {
            surface = _latest ?? _lastSent;
            if (_latest is not null)
            {
                _lastSent = _latest;
                _latest = null;
            }
            _frameArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        if (surface is null)
        {
            // Nothing captured yet: wait for the first frame.
            return await NextVideoSampleAsync();
        }
        return MediaStreamSample.CreateFromDirect3D11Surface(surface, _clock.Elapsed);
    }

    /// <summary>The next 10 ms of mixed audio, paced to real time; null at the end.</summary>
    private async Task<MediaStreamSample?> NextAudioSampleAsync()
    {
        var timestamp = AudioBlock * _audioBlocks;
        var due = timestamp + AudioBlock;
        var wait = due - _clock.Elapsed;
        if (wait > TimeSpan.Zero)
        {
            try { await Task.Delay(wait, _stop.Token); }
            catch (TaskCanceledException) { return null; }
        }
        if (_stop.IsCancellationRequested) return null;

        var mix = new float[AudioBlockFrames * WasapiSource.Channels];
        lock (_audioLock)
        {
            _system?.MixInto(mix, AudioBlockFrames);
            _mic?.MixInto(mix, AudioBlockFrames);
        }
        for (var i = 0; i < mix.Length; i++) mix[i] = Math.Clamp(mix[i], -1f, 1f);

        var bytes = new byte[mix.Length * 4];
        Buffer.BlockCopy(mix, 0, bytes, 0, bytes.Length);
        var sample = MediaStreamSample.CreateFromBuffer(bytes.AsBuffer(), timestamp);
        sample.Duration = AudioBlock;
        _audioBlocks++;
        return sample;
    }

    /// <summary>Stops recording and finishes the file. Returns the length recorded.</summary>
    public async Task<TimeSpan> StopAsync()
    {
        var length = _clock.Elapsed;
        _stop.Cancel();
        _clock.Stop();
        lock (_frameLock) _frameArrived.TrySetResult();
        _session.Dispose();
        _pool.Dispose();
        SetSystemAudio(false);
        SetMicrophone(false);
        if (_transcode is not null)
        {
            try { await _transcode; }
            catch (Exception ex) { Log.Error("Finishing the recording failed", ex); throw; }
        }
        Log.Info($"Recorded {_crop.Width}x{_crop.Height}, {length:m\\:ss\\.f} to {_path}");
        return length;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stop.IsCancellationRequested) await StopAsync();
        _d3d.Dispose();
    }
}
