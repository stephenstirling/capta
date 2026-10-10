using System.Runtime.InteropServices;

namespace Capta.Capture;

/// <summary>
/// Converts 8-bit BGRA pixels from one ICC profile to another with WIC's colour transform (the
/// Windows colour engine, which handles matrix and LUT display profiles alike). COM calls go
/// through vtable slots verified against the Windows SDK wincodec.h, as in <see cref="JpegXr"/>.
/// </summary>
internal static unsafe partial class WicColour
{
    private const uint CLSCTX_INPROC_SERVER = 1;
    private const uint COINIT_MULTITHREADED = 0;
    private const uint ExifColorSpaceSrgb = 1;

    // vtable slots (see wincodec.h *Vtbl structs)
    private const int Release = 2;
    private const int Factory_CreateColorContext = 15;
    private const int Factory_CreateColorTransformer = 16;
    private const int Factory_CreateBitmapFromMemory = 20;
    private const int Context_InitializeFromMemory = 4;
    private const int Context_InitializeFromExifColorSpace = 5;
    private const int Source_CopyPixels = 7;
    private const int Transform_Initialize = 8;

    private static readonly Guid CLSID_WICImagingFactory = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    private static readonly Guid IID_IWICImagingFactory = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
    // 32bppBGR: the fourth byte is ignored, so alpha is restored by the caller.
    private static readonly Guid GUID_WICPixelFormat32bppBGR = new("6fddc324-4e03-4bfe-b185-3d77768dc90e");

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint coInit);

    /// <param name="bgra">Top-down BGRA, no row padding. The alpha bytes of the result are undefined.</param>
    /// <param name="from">ICC profile the pixels are in; null for sRGB.</param>
    /// <param name="to">ICC profile to convert to; null for sRGB.</param>
    public static byte[] Convert(byte[] bgra, int width, int height, byte[]? from, byte[]? to)
    {
        if (bgra.Length != width * height * 4)
            throw new ArgumentException("Pixel buffer does not match dimensions.", nameof(bgra));
        CoInitializeEx(0, COINIT_MULTITHREADED); // S_FALSE / RPC_E_CHANGED_MODE are fine

        nint factory = 0, bitmap = 0, source = 0, dest = 0, transform = 0;
        try
        {
            Check(CoCreateInstance(in CLSID_WICImagingFactory, 0, CLSCTX_INPROC_SERVER, in IID_IWICImagingFactory, out factory));

            var format = GUID_WICPixelFormat32bppBGR;
            var stride = (uint)width * 4;
            fixed (byte* pixels = bgra) // copied into the bitmap
                Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, Guid*, uint, uint, byte*, nint*, int>)Slot(factory, Factory_CreateBitmapFromMemory))(
                    factory, (uint)width, (uint)height, &format, stride, (uint)bgra.Length, pixels, &bitmap));

            var output = new byte[bgra.Length];
            // Pinned until the transform is done, in case the contexts read the profiles in place.
            fixed (byte* fromIcc = from, toIcc = to, p = output)
            {
                source = CreateContext(factory, fromIcc, from?.Length ?? 0);
                dest = CreateContext(factory, toIcc, to?.Length ?? 0);
                Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(factory, Factory_CreateColorTransformer))(factory, &transform));
                Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, nint, Guid*, int>)Slot(transform, Transform_Initialize))(
                    transform, bitmap, source, dest, &format));
                Check(((delegate* unmanaged[Stdcall]<nint, void*, uint, uint, byte*, int>)Slot(transform, Source_CopyPixels))(
                    transform, null, stride, (uint)output.Length, p));
            }
            return output;
        }
        finally
        {
            foreach (var unknown in new[] { transform, dest, source, bitmap, factory })
                if (unknown != 0)
                    ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(unknown, Release))(unknown);
        }
    }

    /// <param name="profile">ICC profile bytes; null for sRGB.</param>
    private static nint CreateContext(nint factory, byte* profile, int length)
    {
        nint context;
        Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(factory, Factory_CreateColorContext))(factory, &context));
        try
        {
            if (profile is null)
                Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(context, Context_InitializeFromExifColorSpace))(context, ExifColorSpaceSrgb));
            else
                Check(((delegate* unmanaged[Stdcall]<nint, byte*, uint, int>)Slot(context, Context_InitializeFromMemory))(context, profile, (uint)length));
            return context;
        }
        catch
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(context, Release))(context);
            throw;
        }
    }

    private static void* Slot(nint unknown, int index) => (*(void***)unknown)[index];

    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
}
