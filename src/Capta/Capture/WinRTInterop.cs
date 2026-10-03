using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.Display;

namespace Capta.Capture;

/// <summary>
/// Desktop interop factories for WinRT types that are normally bound to a CoreWindow.
/// Interface layouts verified against Windows.Graphics.Capture.Interop.h and
/// windows.graphics.display.interop.idl.
/// </summary>
internal static unsafe partial class WinRTInterop
{
    private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
    private static readonly Guid IID_IGraphicsCaptureItem = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");
    private static readonly Guid IID_IDisplayInformationStaticsInterop = new("7449121c-382b-4705-8da7-a795ba482013");
    private static readonly Guid IID_IDisplayInformation = new("bed112ae-adc3-4dc9-ae65-851f4d7d4799");

    // IGraphicsCaptureItemInterop : IUnknown
    private const int CaptureInterop_CreateForWindow = 3;
    private const int CaptureInterop_CreateForMonitor = 4;
    // IDisplayInformationStaticsInterop : IInspectable
    private const int DisplayInterop_GetForMonitor = 7;

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(nint activatableClassId, Guid* iid, nint* factory);

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string sourceString, int length, nint* hstring);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint hstring);

    public static GraphicsCaptureItem CreateItemForMonitor(nint hmonitor) =>
        CreateItem(CaptureInterop_CreateForMonitor, hmonitor);

    public static GraphicsCaptureItem CreateItemForWindow(nint hwnd) =>
        CreateItem(CaptureInterop_CreateForWindow, hwnd);

    private static GraphicsCaptureItem CreateItem(int slot, nint handle)
    {
        var factory = GetFactory("Windows.Graphics.Capture.GraphicsCaptureItem", IID_IGraphicsCaptureItemInterop);
        try
        {
            nint item;
            var iid = IID_IGraphicsCaptureItem;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)(*(nint**)factory)[slot])(factory, handle, &iid, &item));
            try
            {
                return GraphicsCaptureItem.FromAbi(item);
            }
            finally
            {
                Marshal.Release(item);
            }
        }
        finally
        {
            Marshal.Release(factory);
        }
    }

    /// <summary>Windows.Graphics.Display.DisplayInformation for a specific monitor.</summary>
    public static DisplayInformation GetDisplayInformation(nint hmonitor)
    {
        var factory = GetFactory("Windows.Graphics.Display.DisplayInformation", IID_IDisplayInformationStaticsInterop);
        try
        {
            nint info;
            var iid = IID_IDisplayInformation;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)(*(nint**)factory)[DisplayInterop_GetForMonitor])(factory, hmonitor, &iid, &info));
            try
            {
                return DisplayInformation.FromAbi(info);
            }
            finally
            {
                Marshal.Release(info);
            }
        }
        finally
        {
            Marshal.Release(factory);
        }
    }

    private static nint GetFactory(string className, Guid iid)
    {
        nint hstring;
        Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, &hstring));
        try
        {
            nint factory;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, &iid, &factory));
            return factory;
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }
}
