using Capta.Capture;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Capta.Services;

/// <summary>PNG encoding and clipboard/file output for captures.</summary>
public static class ImageExport
{
    public static async Task<InMemoryRandomAccessStream> EncodePngAsync(CapturedImage image)
    {
        var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            (uint)image.Width, (uint)image.Height, 96, 96, image.Pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        return stream;
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

    public static async Task SaveAsync(CapturedImage image, StorageFile file)
    {
        using var png = await EncodePngAsync(image);
        using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
        output.Size = 0;
        await RandomAccessStream.CopyAndCloseAsync(png.GetInputStreamAt(0), output.GetOutputStreamAt(0));
    }

    public static string DefaultFileName() => $"Capta {DateTime.Now:yyyy-MM-dd HHmmss}";
}
