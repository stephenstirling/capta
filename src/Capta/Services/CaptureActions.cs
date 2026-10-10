using System.Runtime.InteropServices;
using Capta.Capture;
using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace Capta.Services;

/// <summary>Actions on a finished capture: Edit, Save, Copy variants and Ocula hand-off.</summary>
public static partial class CaptureActions
{
    /// <summary>
    /// Hands the capture to Ocula via <c>ocula:edit?file=&lt;path&gt;</c>. Until Ocula is
    /// installed (nothing handles the scheme), opens the default image app instead.
    /// </summary>
    public static async Task EditAsync(CaptureResult capture)
    {
        var file = await SaveTempAsync(capture, "Edit");
        var ocula = OculaUri("edit", file.Path);
        if (await IsOculaInstalledAsync())
            await Launcher.LaunchUriAsync(ocula);
        else
            await Launcher.LaunchFileAsync(file);
    }

    /// <summary>Adds the capture to Ocula's library via <c>ocula:import?file=&lt;path&gt;</c>.</summary>
    public static async Task SendToOculaAsync(CaptureResult capture)
    {
        var path = capture.VideoPath ?? (await SaveTempAsync(capture, "Ocula")).Path;
        await Launcher.LaunchUriAsync(OculaUri("import", path));
    }

    /// <summary>Copies a recording as a file (pastes into Explorer, email and chat).</summary>
    public static async Task CopyFileAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetStorageItems([file]);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    /// <summary>Save As for a recording: copies the MP4. Returns the new path, or null if cancelled.</summary>
    public static async Task<string?> SaveVideoAsAsync(CaptureResult capture, WindowId owner)
    {
        var source = capture.VideoPath ?? throw new InvalidOperationException("Not a recording.");
        var picker = new FileSavePicker(owner)
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(source),
            SuggestedFolder = CaptureFolderOrScreenshots(),
            DefaultFileExtension = ".mp4",
        };
        picker.FileTypeChoices.Add("MP4 video", [".mp4"]);
        var result = await picker.PickSaveFileAsync();
        if (result is null) return null;
        if (!string.Equals(Path.GetFullPath(result.Path), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
            await Task.Run(() => File.Copy(source, result.Path, overwrite: true));
        return result.Path;
    }

    /// <summary>Where new recordings go: the capture folder.</summary>
    public static string NewRecordingPath() =>
        Path.Combine(CaptureFolderOrScreenshots(), $"Recording {DateTime.Now:yyyy-MM-dd HHmmss}.mp4");

    public static async Task<bool> IsOculaInstalledAsync() =>
        await Launcher.QueryUriSupportAsync(new Uri("ocula:"), LaunchQuerySupportType.Uri) == LaunchQuerySupportStatus.Available;

    private static Uri OculaUri(string verb, string path) => new($"ocula:{verb}?file={Uri.EscapeDataString(path)}");

    /// <summary>
    /// Copies the capture as a PNG file (pastes into Explorer, email and chat apps). Always sRGB,
    /// like the rest of the clipboard: many of those apps ignore colour profiles.
    /// </summary>
    public static async Task CopyAsFileAsync(CaptureResult capture)
    {
        var file = await SaveTempAsync(capture, "Clipboard", ColourSpace.Srgb);
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetStorageItems([file]);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    public static void CopyText(string text)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    /// <summary>Recognises text with on-device Windows OCR and copies it. Returns the text, or null if none was found.</summary>
    public static async Task<string?> CopyTextInImageAsync(CapturedImage image)
    {
        var text = await Ocr.RecognizeAsync(image);
        if (text is not null) CopyText(text);
        return text;
    }

    private static async Task<StorageFile> SaveTempAsync(CaptureResult capture, string folderName, ColourSpace? space = null)
    {
        var folder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync(folderName, CreationCollisionOption.OpenIfExists);
        var file = await folder.CreateFileAsync(ImageExport.DefaultFileName() + ".png", CreationCollisionOption.GenerateUniqueName);
        await SaveWithMetadataAsync(capture, file, space);
        return file;
    }

    /// <summary>Saves a PNG carrying the capture's metadata (source, title, mode, OCR text).</summary>
    /// <param name="space">Null: the colour space chosen in Colour &amp; HDR.</param>
    public static async Task SaveWithMetadataAsync(CaptureResult capture, StorageFile file, ColourSpace? space = null)
    {
        var metadata = await CaptureMetadata.ForAsync(capture);
        await ImageExport.SaveAsync(capture.Image, file, metadata.ToJson(), space);
    }

    /// <summary>
    /// Save As dialog in the capture folder. Captures of an HDR screen can also be saved as JPEG XR;
    /// Colour &amp; HDR decides the default and whether a .jxr goes beside the PNG. Returns the saved
    /// path, or null if cancelled.
    /// </summary>
    public static async Task<string?> SaveAsAsync(CaptureResult capture, WindowId owner)
    {
        var hdr = capture.Image.HdrPixels is not null;
        var handling = Settings.HdrHandling;
        var hdrFirst = hdr && handling == HdrHandling.KeepHdr;
        var picker = new FileSavePicker(owner)
        {
            SuggestedFileName = ImageExport.DefaultFileName(),
            SuggestedFolder = CaptureFolderOrScreenshots(),
            DefaultFileExtension = hdrFirst ? ".jxr" : ".png",
        };
        if (hdrFirst) picker.FileTypeChoices.Add(JpegXrChoice, [".jxr"]);
        picker.FileTypeChoices.Add("PNG image", [".png"]);
        if (hdr && !hdrFirst) picker.FileTypeChoices.Add(JpegXrChoice, [".jxr"]);

        var result = await picker.PickSaveFileAsync();
        if (result is null) return null;

        if (hdr && Path.GetExtension(result.Path).Equals(".jxr", StringComparison.OrdinalIgnoreCase))
        {
            await SaveHdrAsync(capture.Image, result.Path);
            return result.Path;
        }

        var file = await StorageFile.GetFileFromPathAsync(EnsureFileExists(result.Path));
        await SaveWithMetadataAsync(capture, file);
        if (hdr && handling == HdrHandling.SaveBoth)
            await SaveHdrAsync(capture.Image, Path.ChangeExtension(result.Path, ".jxr"));
        return result.Path;
    }

    private const string JpegXrChoice = "JPEG XR image (HDR)";

    /// <summary>Copies the HDR original as a .jxr file (pastes into HDR-aware apps, Explorer and chat).</summary>
    public static async Task CopyAsHdrAsync(CaptureResult capture)
    {
        var folder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("Clipboard", CreationCollisionOption.OpenIfExists);
        var file = await folder.CreateFileAsync(ImageExport.DefaultFileName() + ".jxr", CreationCollisionOption.GenerateUniqueName);
        await SaveHdrAsync(capture.Image, file.Path);
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetStorageItems([file]);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private static Task SaveHdrAsync(CapturedImage image, string path)
    {
        var pixels = image.HdrPixels ?? throw new InvalidOperationException("This capture has no HDR original.");
        return Task.Run(() => JpegXr.Save(pixels, image.Width, image.Height, path));
    }

    private static string EnsureFileExists(string path)
    {
        if (!File.Exists(path)) File.Create(path).Dispose();
        return path;
    }

    private static readonly Guid FOLDERID_Screenshots = new("b7bede81-df94-4682-a7d8-57a52620b86f");

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid rfid, uint flags, nint token, out nint path);

    /// <summary>The chosen capture folder (created if needed), falling back to Pictures\Screenshots.</summary>
    private static string CaptureFolderOrScreenshots()
    {
        try
        {
            return Directory.CreateDirectory(Settings.CaptureFolder).FullName;
        }
        catch (Exception ex)
        {
            Log.Error("Capture folder unavailable", ex);
            return ScreenshotsFolder();
        }
    }

    /// <summary>Pictures\Screenshots (honouring folder redirection), creating it if needed.</summary>
    public static string ScreenshotsFolder()
    {
        string? path = null;
        if (SHGetKnownFolderPath(FOLDERID_Screenshots, 0x8000 /* KF_FLAG_CREATE */, 0, out var p) >= 0)
        {
            path = Marshal.PtrToStringUni(p);
            Marshal.FreeCoTaskMem(p);
        }
        return path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
    }
}
