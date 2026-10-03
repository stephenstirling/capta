using System.Runtime.InteropServices;
using Capta.Capture;
using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;
using Windows.Storage;
using Windows.System;

namespace Capta.Services;

/// <summary>Edit and Save for a finished capture.</summary>
public static partial class CaptureActions
{
    /// <summary>
    /// Hands the capture to Ocula via <c>ocula:edit?file=&lt;path&gt;</c>. Until Ocula is
    /// installed (nothing handles the scheme), opens the default image app instead.
    /// </summary>
    public static async Task EditAsync(CapturedImage image)
    {
        var folder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("Edit", CreationCollisionOption.OpenIfExists);
        var file = await folder.CreateFileAsync(ImageExport.DefaultFileName() + ".png", CreationCollisionOption.GenerateUniqueName);
        await ImageExport.SaveAsync(image, file);

        var ocula = new Uri($"ocula:edit?file={Uri.EscapeDataString(file.Path)}");
        var support = await Launcher.QueryUriSupportAsync(ocula, LaunchQuerySupportType.Uri);
        if (support == LaunchQuerySupportStatus.Available)
            await Launcher.LaunchUriAsync(ocula);
        else
            await Launcher.LaunchFileAsync(file);
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
