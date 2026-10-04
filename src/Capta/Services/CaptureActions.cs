using System.Runtime.InteropServices;
using Capta.Capture;
using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Ocr;
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
    public static async Task EditAsync(CapturedImage image)
    {
        var file = await SaveTempAsync(image, "Edit");
        var ocula = OculaUri("edit", file.Path);
        if (await IsOculaInstalledAsync())
            await Launcher.LaunchUriAsync(ocula);
        else
            await Launcher.LaunchFileAsync(file);
    }

    /// <summary>Adds the capture to Ocula's library via <c>ocula:import?file=&lt;path&gt;</c>.</summary>
    public static async Task SendToOculaAsync(CapturedImage image)
    {
        var file = await SaveTempAsync(image, "Ocula");
        await Launcher.LaunchUriAsync(OculaUri("import", file.Path));
    }

    public static async Task<bool> IsOculaInstalledAsync() =>
        await Launcher.QueryUriSupportAsync(new Uri("ocula:"), LaunchQuerySupportType.Uri) == LaunchQuerySupportStatus.Available;

    private static Uri OculaUri(string verb, string path) => new($"ocula:{verb}?file={Uri.EscapeDataString(path)}");

    /// <summary>Copies the capture as a PNG file (pastes into Explorer, email and chat apps).</summary>
    public static async Task CopyAsFileAsync(CapturedImage image)
    {
        var file = await SaveTempAsync(image, "Clipboard");
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
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("No OCR language is installed. Add one in Settings > Time & language.");
        using var bitmap = image.ToSoftwareBitmap();
        var result = await engine.RecognizeAsync(bitmap);
        var text = string.Join(Environment.NewLine, result.Lines.Select(l => l.Text));
        if (string.IsNullOrWhiteSpace(text)) return null;
        CopyText(text);
        return text;
    }

    private static async Task<StorageFile> SaveTempAsync(CapturedImage image, string folderName)
    {
        var folder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync(folderName, CreationCollisionOption.OpenIfExists);
        var file = await folder.CreateFileAsync(ImageExport.DefaultFileName() + ".png", CreationCollisionOption.GenerateUniqueName);
        await ImageExport.SaveAsync(image, file);
        return file;
    }

    /// <summary>Save As dialog defaulting to Pictures\Screenshots. Returns the saved path, or null if cancelled.</summary>
    public static async Task<string?> SaveAsAsync(CapturedImage image, WindowId owner)
    {
        var picker = new FileSavePicker(owner)
        {
            SuggestedFileName = ImageExport.DefaultFileName(),
            SuggestedFolder = ScreenshotsFolder(),
            DefaultFileExtension = ".png",
        };
        picker.FileTypeChoices.Add("PNG image", [".png"]);

        var result = await picker.PickSaveFileAsync();
        if (result is null) return null;

        var file = await StorageFile.GetFileFromPathAsync(EnsureFileExists(result.Path));
        await ImageExport.SaveAsync(image, file);
        return result.Path;
    }

    private static string EnsureFileExists(string path)
    {
        if (!File.Exists(path)) File.Create(path).Dispose();
        return path;
    }

    private static readonly Guid FOLDERID_Screenshots = new("b7bede81-df94-4682-a7d8-57a52620b86f");

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid rfid, uint flags, nint token, out nint path);

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
