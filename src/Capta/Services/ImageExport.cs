using System.Runtime.InteropServices.WindowsRuntime;
using Capta.Capture;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Capta.Services;

/// <summary>PNG encoding and clipboard/file output for captures.</summary>
public static class ImageExport
{
    /// <param name="captaJson">Capture metadata for the "Capta" tEXt chunk (saved files only).</param>
    /// <param name="target">sRGB for the clipboard and Recent; saved files use the Colour &amp; HDR choice.</param>
    public static async Task<InMemoryRandomAccessStream> EncodePngAsync(CapturedImage image, string? captaJson = null,
        ColourSpace target = ColourSpace.Srgb)
    {
        var (pixels, space) = await ColourOutput.ConvertAsync(image, target);
        var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, image.HasTransparency ? BitmapAlphaMode.Premultiplied : BitmapAlphaMode.Ignore,
            (uint)image.Width, (uint)image.Height, 96, 96, pixels);
        if (space == ColourSpace.Srgb && CaptureOptions.Current.EmbedColourProfile)
        {
            try
            {
                // PNG sRGB chunk (rendering intent 0, perceptual): the pixels are sRGB.
                await encoder.BitmapProperties.SetPropertiesAsync(new Dictionary<string, BitmapTypedValue>
                {
                    ["/sRGB/RenderingIntent"] = new BitmapTypedValue((byte)0, Windows.Foundation.PropertyType.UInt8),
                });
            }
            catch (Exception)
            {
                // The encoder doesn't support the tag here; the PNG is still sRGB.
            }
        }
        if (captaJson is not null)
        {
            try
            {
                await encoder.BitmapProperties.SetPropertiesAsync(new Dictionary<string, BitmapTypedValue>
                {
                    ["/tEXt/{str=" + CaptureMetadata.PngKeyword + "}"] = new BitmapTypedValue(captaJson, Windows.Foundation.PropertyType.String),
                });
            }
            catch (Exception ex)
            {
                Log.Error("Couldn't write capture metadata", ex);
            }
        }
        await encoder.FlushAsync();
        if (space == ColourSpace.DisplayP3)
            return await WithIccProfileAsync(stream, "Display P3", ColourOutput.DisplayP3Profile);
        stream.Seek(0);
        return stream;
    }

    /// <summary>Adds an iCCP chunk, which the WinRT encoder can't write. Disposes <paramref name="png"/>.</summary>
    private static async Task<InMemoryRandomAccessStream> WithIccProfileAsync(InMemoryRandomAccessStream png, string name, byte[] profile)
    {
        byte[] bytes;
        using (png)
        {
            bytes = new byte[png.Size];
            using var reader = new DataReader(png.GetInputStreamAt(0));
            await reader.LoadAsync((uint)png.Size);
            reader.ReadBytes(bytes);
        }
        var tagged = new InMemoryRandomAccessStream();
        await tagged.WriteAsync(PngChunks.WithIccProfile(bytes, name, profile).AsBuffer());
        tagged.Seek(0);
        return tagged;
    }

    /// <summary>
    /// Puts the capture on the clipboard as a "PNG" stream (lossless, preferred by Office,
    /// browsers and image editors) plus the standard bitmap format for everything else.
    /// Must be called on the UI thread.
    /// </summary>
    public static async Task CopyToClipboardAsync(CapturedImage image)
    {
        var png = await EncodePngAsync(image);
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetData("PNG", png.CloneStream());
        package.SetBitmap(RandomAccessStreamReference.CreateFromStream(png));
        Clipboard.SetContent(package);
        // Keep the data available after Capta exits.
        Clipboard.Flush();
    }

    /// <summary>Saves a PNG in the colour space chosen in Colour &amp; HDR.</summary>
    public static async Task SaveAsync(CapturedImage image, StorageFile file, string? captaJson = null)
    {
        using var png = await EncodePngAsync(image, captaJson, CaptureOptions.Current.ColourSpace);
        using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
        output.Size = 0;
        await RandomAccessStream.CopyAndCloseAsync(png.GetInputStreamAt(0), output.GetOutputStreamAt(0));
    }

    public static string DefaultFileName() => $"Capta {DateTime.Now:yyyy-MM-dd HHmmss}";
}
