using System.Runtime.InteropServices.WindowsRuntime;
using Capta.Capture;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Capta.Recording;

/// <summary>Thumbnails and GIF export for recordings (Windows.Media.Editing).</summary>
public static class VideoTools
{
    /// <summary>A frame near the start, as an image for the card and Recent.</summary>
    public static async Task<CapturedImage> ThumbnailAsync(string path, int width, int height)
    {
        var composition = await CompositionAsync(path);
        var at = TimeSpan.FromMilliseconds(Math.Min(300, composition.Duration.TotalMilliseconds / 2));
        var stream = await composition.GetThumbnailAsync(at, width, height, VideoFramePrecision.NearestFrame);
        return await DecodeAsync(stream);
    }

    /// <summary>
    /// Writes an animated, looping GIF of the recording: up to <paramref name="maxSeconds"/>,
    /// <paramref name="fps"/> frames a second, scaled to fit <paramref name="maxWidth"/>.
    /// </summary>
    public static async Task SaveGifAsync(string videoPath, string gifPath, int maxWidth = 800, int fps = 10, int maxSeconds = 30,
        IProgress<double>? progress = null)
    {
        var composition = await CompositionAsync(videoPath);
        var clip = composition.Clips[0];
        var props = clip.GetVideoEncodingProperties();
        double scale = Math.Min(1, maxWidth / (double)props.Width);
        int width = Math.Max(2, (int)(props.Width * scale)), height = Math.Max(2, (int)(props.Height * scale));

        var duration = TimeSpan.FromSeconds(Math.Min(maxSeconds, composition.Duration.TotalSeconds));
        var count = Math.Max(1, (int)(duration.TotalSeconds * fps));
        var delay = (ushort)Math.Round(100.0 / fps); // GIF delays are in 1/100 s

        Directory.CreateDirectory(Path.GetDirectoryName(gifPath)!);
        var file = await StorageFile.GetFileFromPathAsync(EnsureFile(gifPath));
        using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
        output.Size = 0;
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.GifEncoderId, output);
        // Loop forever (NETSCAPE2.0 application extension).
        await encoder.BitmapContainerProperties.SetPropertiesAsync(new Dictionary<string, BitmapTypedValue>
        {
            ["/appext/Application"] = new BitmapTypedValue("NETSCAPE2.0"u8.ToArray(), Windows.Foundation.PropertyType.UInt8Array),
            ["/appext/Data"] = new BitmapTypedValue(new byte[] { 3, 1, 0, 0, 0 }, Windows.Foundation.PropertyType.UInt8Array),
        });

        for (var i = 0; i < count; i++)
        {
            var at = TimeSpan.FromSeconds(i / (double)fps);
            using var stream = await composition.GetThumbnailAsync(at, width, height, VideoFramePrecision.NearestFrame);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            // Copy the pixels: SetSoftwareBitmap keeps a reference, and this bitmap is disposed
            // before the frame is committed by the next GoToNextFrameAsync.
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyToBuffer(pixels.AsBuffer());
            if (i > 0) await encoder.GoToNextFrameAsync();
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
            await encoder.BitmapProperties.SetPropertiesAsync(new Dictionary<string, BitmapTypedValue>
            {
                ["/grctlext/Delay"] = new BitmapTypedValue(delay, Windows.Foundation.PropertyType.UInt16),
            });
            progress?.Report((i + 1) / (double)count);
        }
        await encoder.FlushAsync();
    }

    private static async Task<MediaComposition> CompositionAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var composition = new MediaComposition();
        composition.Clips.Add(await MediaClip.CreateFromFileAsync(file));
        return composition;
    }

    private static async Task<CapturedImage> DecodeAsync(IRandomAccessStream stream)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyToBuffer(pixels.AsBuffer());
        // Video frames are opaque.
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        return new CapturedImage(bitmap.PixelWidth, bitmap.PixelHeight, pixels);
    }

    private static string EnsureFile(string path)
    {
        if (!File.Exists(path)) File.Create(path).Dispose();
        return path;
    }
}
