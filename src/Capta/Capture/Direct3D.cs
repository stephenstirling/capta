using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Capta.Capture;

/// <summary>
/// Minimal D3D11 plumbing for Windows.Graphics.Capture: creates a device, wraps it
/// for WinRT, and reads FP16 frames back to the CPU. COM calls go through vtable
/// slots verified against the Windows SDK d3d11.h.
/// </summary>
internal sealed unsafe partial class Direct3D : IDisposable
{
    private const int D3D_DRIVER_TYPE_HARDWARE = 1;
    private const int D3D_DRIVER_TYPE_WARP = 5;
    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    private const uint D3D11_SDK_VERSION = 7;
    private const uint D3D11_USAGE_STAGING = 3;
    private const uint D3D11_CPU_ACCESS_READ = 0x20000;
    private const uint D3D11_MAP_READ = 1;
    private const uint DXGI_FORMAT_R16G16B16A16_FLOAT = 10;
    private const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    private const uint D3D11_USAGE_DEFAULT = 0;
    private const uint D3D11_BIND_SHADER_RESOURCE = 0x8;
    private const uint D3D11_BIND_RENDER_TARGET = 0x20;

    // vtable slots (see d3d11.h *Vtbl structs)
    private const int Device_CreateTexture2D = 5;
    private const int Context_Map = 14;
    private const int Context_Unmap = 15;
    private const int Context_CopySubresourceRegion = 46;
    private const int Context_CopyResource = 47;
    private const int Texture2D_GetDesc = 10;
    private const int DxgiAccess_GetInterface = 3;

    private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");
    private static readonly Guid IID_IDXGISurface = new("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_TEXTURE2D_DESC
    {
        public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CPUAccessFlags, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_MAPPED_SUBRESOURCE
    {
        public void* pData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, int* featureLevels, uint featureLevelCount, uint sdkVersion, nint* device, int* featureLevel, nint* context);

    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, nint* graphicsDevice);

    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11SurfaceFromDXGISurface(nint dxgiSurface, nint* graphicsSurface);

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_BOX
    {
        public uint Left, Top, Front, Right, Bottom, Back;
    }

    private readonly object _contextLock = new();
    private nint _device;
    private nint _context;

    public IDirect3DDevice WinRTDevice { get; }

    private Direct3D(nint device, nint context, IDirect3DDevice winrt)
    {
        _device = device;
        _context = context;
        WinRTDevice = winrt;
    }

    public static Direct3D Create()
    {
        nint device, context;
        int level;
        var hr = D3D11CreateDevice(0, D3D_DRIVER_TYPE_HARDWARE, 0, D3D11_CREATE_DEVICE_BGRA_SUPPORT, null, 0, D3D11_SDK_VERSION, &device, &level, &context);
        if (hr < 0)
            hr = D3D11CreateDevice(0, D3D_DRIVER_TYPE_WARP, 0, D3D11_CREATE_DEVICE_BGRA_SUPPORT, null, 0, D3D11_SDK_VERSION, &device, &level, &context);
        Marshal.ThrowExceptionForHR(hr);

        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in IID_IDXGIDevice, out var dxgi));
        nint inspectable;
        try
        {
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, &inspectable));
        }
        finally
        {
            Marshal.Release(dxgi);
        }

        var winrt = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        Marshal.Release(inspectable);
        return new Direct3D(device, context, winrt);
    }

    /// <summary>Copies an R16G16B16A16_FLOAT surface to a tightly packed array of half-float bits.</summary>
    public ushort[] ReadFp16(IDirect3DSurface surface, out int width, out int height)
    {
        var texture = GetTexture(surface);
        nint staging = 0;
        try
        {
            D3D11_TEXTURE2D_DESC desc;
            ((delegate* unmanaged[Stdcall]<nint, D3D11_TEXTURE2D_DESC*, void>)VTable(texture, Texture2D_GetDesc))(texture, &desc);
            if (desc.Format != DXGI_FORMAT_R16G16B16A16_FLOAT)
                throw new InvalidOperationException($"Unexpected capture format {desc.Format}.");

            width = (int)desc.Width;
            height = (int)desc.Height;

            var stagingDesc = desc;
            stagingDesc.MipLevels = 1;
            stagingDesc.ArraySize = 1;
            stagingDesc.SampleCount = 1;
            stagingDesc.SampleQuality = 0;
            stagingDesc.Usage = D3D11_USAGE_STAGING;
            stagingDesc.BindFlags = 0;
            stagingDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
            stagingDesc.MiscFlags = 0;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, D3D11_TEXTURE2D_DESC*, void*, nint*, int>)VTable(_device, Device_CreateTexture2D))(_device, &stagingDesc, null, &staging));

            var result = new ushort[width * height * 4];
            var rowElements = width * 4;

            // The immediate context is not thread-safe; free-threaded frame pools call us from the pool.
            lock (_contextLock)
            {
                ((delegate* unmanaged[Stdcall]<nint, nint, nint, void>)VTable(_context, Context_CopyResource))(_context, staging, texture);

                D3D11_MAPPED_SUBRESOURCE mapped;
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, D3D11_MAPPED_SUBRESOURCE*, int>)VTable(_context, Context_Map))(_context, staging, 0, D3D11_MAP_READ, 0, &mapped));
                try
                {
                    for (var y = 0; y < height; y++)
                    {
                        var src = new ReadOnlySpan<ushort>((byte*)mapped.pData + (long)y * mapped.RowPitch, rowElements);
                        src.CopyTo(result.AsSpan(y * rowElements, rowElements));
                    }
                }
                finally
                {
                    ((delegate* unmanaged[Stdcall]<nint, nint, uint, void>)VTable(_context, Context_Unmap))(_context, staging, 0);
                }
            }
            return result;
        }
        finally
        {
            if (staging != 0) Marshal.Release(staging);
            Marshal.Release(texture);
        }
    }

    /// <summary>
    /// Copies part of a BGRA surface into a new <paramref name="width"/> x <paramref name="height"/>
    /// texture (for video frames: the pool recycles its surfaces). The source rectangle is clipped
    /// to the surface; anything it doesn't cover stays black.
    /// </summary>
    public IDirect3DSurface CopyRegion(IDirect3DSurface source, int x, int y, int width, int height)
    {
        var texture = GetTexture(source);
        nint copy = 0, dxgi = 0;
        try
        {
            D3D11_TEXTURE2D_DESC srcDesc;
            ((delegate* unmanaged[Stdcall]<nint, D3D11_TEXTURE2D_DESC*, void>)VTable(texture, Texture2D_GetDesc))(texture, &srcDesc);

            var desc = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleCount = 1,
                Usage = D3D11_USAGE_DEFAULT,
                BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE,
            };
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, D3D11_TEXTURE2D_DESC*, void*, nint*, int>)VTable(_device, Device_CreateTexture2D))(_device, &desc, null, &copy));

            var box = new D3D11_BOX
            {
                Left = (uint)Math.Clamp(x, 0, (int)srcDesc.Width),
                Top = (uint)Math.Clamp(y, 0, (int)srcDesc.Height),
                Front = 0,
                Back = 1,
            };
            box.Right = (uint)Math.Clamp(x + width, (int)box.Left, (int)srcDesc.Width);
            box.Bottom = (uint)Math.Clamp(y + height, (int)box.Top, (int)srcDesc.Height);
            if (box.Right > box.Left && box.Bottom > box.Top)
            {
                lock (_contextLock)
                    ((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, uint, nint, uint, D3D11_BOX*, void>)VTable(_context, Context_CopySubresourceRegion))(
                        _context, copy, 0, 0, 0, 0, texture, 0, &box);
            }

            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(copy, in IID_IDXGISurface, out dxgi));
            nint inspectable;
            Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(dxgi, &inspectable));
            var surface = MarshalInterface<IDirect3DSurface>.FromAbi(inspectable);
            Marshal.Release(inspectable);
            return surface;
        }
        finally
        {
            if (dxgi != 0) Marshal.Release(dxgi);
            if (copy != 0) Marshal.Release(copy);
            Marshal.Release(texture);
        }
    }

    private static nint GetTexture(IDirect3DSurface surface)
    {
        var unknown = ((IWinRTObject)surface).NativeObject.ThisPtr;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in IID_IDirect3DDxgiInterfaceAccess, out var access));
        try
        {
            nint texture;
            var iid = IID_ID3D11Texture2D;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)VTable(access, DxgiAccess_GetInterface))(access, &iid, &texture));
            return texture;
        }
        finally
        {
            Marshal.Release(access);
        }
    }

    private static nint VTable(nint comObject, int slot) => (*(nint**)comObject)[slot];

    public void Dispose()
    {
        WinRTDevice.Dispose();
        if (_context != 0) { Marshal.Release(_context); _context = 0; }
        if (_device != 0) { Marshal.Release(_device); _device = 0; }
    }
}
