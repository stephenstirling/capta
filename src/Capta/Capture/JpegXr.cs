using System.Runtime.InteropServices;

namespace Capta.Capture;

/// <summary>
/// Writes FP16 scRGB pixels as JPEG XR (.jxr), the HDR still format Windows uses (Game Bar,
/// Photos). WinRT's BitmapEncoder can't take half-float pixels, so this drives WIC directly.
/// COM calls go through vtable slots verified against the Windows SDK wincodec.h.
/// </summary>
internal static unsafe partial class JpegXr
{
    private const uint CLSCTX_INPROC_SERVER = 1;
    private const uint COINIT_MULTITHREADED = 0;
    private const uint GENERIC_WRITE = 0x40000000;
    private const int WICBitmapEncoderNoCache = 2;

    // vtable slots (see wincodec.h *Vtbl structs)
    private const int Release = 2;
    private const int Factory_CreateEncoder = 8;
    private const int Factory_CreateStream = 14;
    private const int Stream_InitializeFromFilename = 15;
    private const int Encoder_Initialize = 3;
    private const int Encoder_CreateNewFrame = 10;
    private const int Encoder_Commit = 11;
    private const int Frame_Initialize = 3;
    private const int Frame_SetSize = 4;
    private const int Frame_SetPixelFormat = 6;
    private const int Frame_WritePixels = 10;
    private const int Frame_Commit = 12;

    private static readonly Guid CLSID_WICImagingFactory = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    private static readonly Guid IID_IWICImagingFactory = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
    private static readonly Guid GUID_ContainerFormatWmp = new("57a37caa-367a-4540-916b-f183c5093a4b");
    private static readonly Guid GUID_WICPixelFormat64bppRGBAHalf = new("6fddc324-4e03-4bfe-b185-3d77768dc93a");

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint coInit);

    /// <param name="rgbaHalf">Top-down RGBA half floats, scRGB (1.0 = 80 nits), no row padding.</param>
    public static void Save(ushort[] rgbaHalf, int width, int height, string path)
    {
        if (rgbaHalf.Length != width * height * 4)
            throw new ArgumentException("Pixel buffer does not match dimensions.", nameof(rgbaHalf));
        CoInitializeEx(0, COINIT_MULTITHREADED); // S_FALSE / RPC_E_CHANGED_MODE are fine

        nint factory = 0, stream = 0, encoder = 0, frame = 0, options = 0;
        try
        {
            Check(CoCreateInstance(in CLSID_WICImagingFactory, 0, CLSCTX_INPROC_SERVER, in IID_IWICImagingFactory, out factory));

            Check(Call(factory, Factory_CreateStream, &stream));
            fixed (char* p = path)
                Check(((delegate* unmanaged[Stdcall]<nint, char*, uint, int>)Slot(stream, Stream_InitializeFromFilename))(stream, p, GENERIC_WRITE));

            var container = GUID_ContainerFormatWmp;
            Check(((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, nint*, int>)Slot(factory, Factory_CreateEncoder))(factory, &container, null, &encoder));
            Check(((delegate* unmanaged[Stdcall]<nint, nint, int, int>)Slot(encoder, Encoder_Initialize))(encoder, stream, WICBitmapEncoderNoCache));
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, nint*, int>)Slot(encoder, Encoder_CreateNewFrame))(encoder, &frame, &options));

            // Default options: the encoder's standard quality, as Windows' own HDR screenshots use.
            Check(Call(frame, Frame_Initialize, options));
            Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, int>)Slot(frame, Frame_SetSize))(frame, (uint)width, (uint)height));
            var format = GUID_WICPixelFormat64bppRGBAHalf;
            Check(((delegate* unmanaged[Stdcall]<nint, Guid*, int>)Slot(frame, Frame_SetPixelFormat))(frame, &format));
            if (format != GUID_WICPixelFormat64bppRGBAHalf)
                throw new NotSupportedException($"The JPEG XR encoder doesn't accept RGBA half floats (offered {format}).");

            var stride = (uint)width * 8;
            fixed (ushort* pixels = rgbaHalf)
                Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, uint, byte*, int>)Slot(frame, Frame_WritePixels))(
                    frame, (uint)height, stride, stride * (uint)height, (byte*)pixels));
            Check(Call(frame, Frame_Commit));
            Check(Call(encoder, Encoder_Commit));
        }
        finally
        {
            foreach (var unknown in new[] { options, frame, encoder, stream, factory })
                if (unknown != 0)
                    ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(unknown, Release))(unknown);
        }
    }

    private static void* Slot(nint unknown, int index) => (*(void***)unknown)[index];

    private static int Call(nint unknown, int index) =>
        ((delegate* unmanaged[Stdcall]<nint, int>)Slot(unknown, index))(unknown);

    private static int Call(nint unknown, int index, nint arg) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(unknown, index))(unknown, arg);

    private static int Call(nint unknown, int index, nint* arg) =>
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(unknown, index))(unknown, arg);

    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
}
