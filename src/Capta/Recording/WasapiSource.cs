using System.Runtime.InteropServices;
using Capta.Services;

namespace Capta.Recording;

/// <summary>
/// Shared-mode WASAPI capture from the default speakers (loopback: what the PC plays) or the
/// default microphone, converted by Windows to 48 kHz stereo float. A background thread drains
/// the device into a buffer that the recorder reads in 10 ms blocks.
/// COM calls go through vtable slots verified against the Windows SDK mmdeviceapi.h/audioclient.h.
/// </summary>
internal sealed unsafe partial class WasapiSource : IDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 2;

    private const uint CLSCTX_ALL = 0x17;
    private const int eRender = 0, eCapture = 1, eConsole = 0;
    private const int AUDCLNT_SHAREMODE_SHARED = 0;
    private const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    private const uint AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;
    private const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
    private const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
    private const ushort WAVE_FORMAT_IEEE_FLOAT = 3;
    private const long BufferDuration = 2_000_000; // 200 ms, in 100 ns units
    private const int MaxBacklogFrames = SampleRate / 10; // drop audio older than 100 ms behind

    // vtable slots
    private const int Release = 2;
    private const int Enumerator_GetDefaultAudioEndpoint = 4;
    private const int Device_Activate = 3;
    private const int Client_Initialize = 3;
    private const int Client_Start = 10;
    private const int Client_Stop = 11;
    private const int Client_GetService = 14;
    private const int Capture_GetBuffer = 3;
    private const int Capture_ReleaseBuffer = 4;
    private const int Capture_GetNextPacketSize = 5;

    private static readonly Guid CLSID_MMDeviceEnumerator = new("bcde0395-e52f-467c-8e3d-c4579291692e");
    private static readonly Guid IID_IMMDeviceEnumerator = new("a95664d2-9614-4f35-a746-de8db63617e6");
    private static readonly Guid IID_IAudioClient = new("1cb9ad4c-dbfa-4c32-b178-c2f568a703b2");
    private static readonly Guid IID_IAudioCaptureClient = new("c8adbd64-e71e-48a0-a4de-185c395cd317");

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag, nChannels;
        public uint nSamplesPerSec, nAvgBytesPerSec;
        public ushort nBlockAlign, wBitsPerSample, cbSize;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint coInit);

    private readonly nint _client;
    private readonly nint _capture;
    private readonly string _name;
    private readonly Queue<float> _buffer = new();
    private readonly Thread _thread;
    private volatile bool _running;

    /// <summary>When set, the device is still drained but contributes silence.</summary>
    public bool Muted { get; set; }

    private WasapiSource(nint client, nint capture, string name)
    {
        _client = client;
        _capture = capture;
        _name = name;
        _thread = new Thread(Pump) { IsBackground = true, Name = $"Capta audio ({name})", Priority = ThreadPriority.AboveNormal };
    }

    /// <summary>Opens the default speakers (loopback) or microphone; null if unavailable.</summary>
    public static WasapiSource? TryOpen(bool loopback)
    {
        var name = loopback ? "system audio" : "microphone";
        CoInitializeEx(0, 0); // MTA; S_FALSE / RPC_E_CHANGED_MODE are fine
        nint enumerator = 0, device = 0, client = 0, capture = 0;
        try
        {
            Check(CoCreateInstance(in CLSID_MMDeviceEnumerator, 0, CLSCTX_ALL, in IID_IMMDeviceEnumerator, out enumerator));
            Check(((delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)Slot(enumerator, Enumerator_GetDefaultAudioEndpoint))(
                enumerator, loopback ? eRender : eCapture, eConsole, &device));
            var iid = IID_IAudioClient;
            Check(((delegate* unmanaged[Stdcall]<nint, Guid*, uint, nint, nint*, int>)Slot(device, Device_Activate))(device, &iid, CLSCTX_ALL, 0, &client));

            var format = new WAVEFORMATEX
            {
                wFormatTag = WAVE_FORMAT_IEEE_FLOAT,
                nChannels = Channels,
                nSamplesPerSec = SampleRate,
                wBitsPerSample = 32,
                nBlockAlign = Channels * 4,
                nAvgBytesPerSec = SampleRate * Channels * 4,
            };
            var flags = AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY
                | (loopback ? AUDCLNT_STREAMFLAGS_LOOPBACK : 0);
            Check(((delegate* unmanaged[Stdcall]<nint, int, uint, long, long, WAVEFORMATEX*, Guid*, int>)Slot(client, Client_Initialize))(
                client, AUDCLNT_SHAREMODE_SHARED, flags, BufferDuration, 0, &format, null));

            var captureIid = IID_IAudioCaptureClient;
            Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(client, Client_GetService))(client, &captureIid, &capture));

            var source = new WasapiSource(client, capture, name);
            client = capture = 0; // owned by the source now
            return source;
        }
        catch (Exception ex)
        {
            Log.Info($"No {name} for recording: {ex.Message}");
            return null;
        }
        finally
        {
            foreach (var unknown in new[] { capture, client, device, enumerator })
                if (unknown != 0) ReleaseCom(unknown);
        }
    }

    public void Start()
    {
        Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(_client, Client_Start))(_client));
        _running = true;
        _thread.Start();
    }

    /// <summary>
    /// Mixes (adds) up to <paramref name="frames"/> frames into <paramref name="mix"/>; missing audio is
    /// silence (loopback delivers nothing while the PC is quiet).
    /// </summary>
    public void MixInto(float[] mix, int frames)
    {
        lock (_buffer)
        {
            // Don't fall behind real time if the device delivered a burst.
            while (_buffer.Count > MaxBacklogFrames * Channels)
                for (var i = 0; i < Channels; i++) _buffer.Dequeue();

            var n = Math.Min(frames * Channels, _buffer.Count);
            for (var i = 0; i < n; i++)
            {
                var v = _buffer.Dequeue();
                if (!Muted) mix[i] += v;
            }
        }
    }

    private void Pump()
    {
        CoInitializeEx(0, 0);
        while (_running)
        {
            try
            {
                uint packet;
                while (((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(_capture, Capture_GetNextPacketSize))(_capture, &packet) >= 0 && packet > 0)
                {
                    byte* data;
                    uint frames, flags;
                    Check(((delegate* unmanaged[Stdcall]<nint, byte**, uint*, uint*, ulong*, ulong*, int>)Slot(_capture, Capture_GetBuffer))(
                        _capture, &data, &frames, &flags, null, null));
                    var samples = (int)frames * Channels;
                    lock (_buffer)
                    {
                        var silent = (flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0;
                        var src = (float*)data;
                        for (var i = 0; i < samples; i++)
                            _buffer.Enqueue(silent ? 0f : src[i]);
                    }
                    ((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(_capture, Capture_ReleaseBuffer))(_capture, frames);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Audio capture ({_name}) failed", ex);
                _running = false;
                return;
            }
            Thread.Sleep(5);
        }
    }

    public void Dispose()
    {
        if (_running)
        {
            _running = false;
            _thread.Join(500);
        }
        ((delegate* unmanaged[Stdcall]<nint, int>)Slot(_client, Client_Stop))(_client);
        ReleaseCom(_capture);
        ReleaseCom(_client);
    }

    private static void* Slot(nint unknown, int index) => (*(void***)unknown)[index];

    private static void ReleaseCom(nint unknown) => ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(unknown, Release))(unknown);

    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
}
