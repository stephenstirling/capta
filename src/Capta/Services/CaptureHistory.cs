using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Capta.Capture;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Capta.Services;

/// <summary>
/// The most recent captures, newest first (the tray flyout's "Recent"). Kept across restarts
/// as PNGs plus a small index in LocalState\Recent; nothing leaves the device.
/// </summary>
public sealed class CaptureHistory
{
    public const int Capacity = 6;

    internal sealed record Entry(
        string File, CaptureMode Mode, bool WasHdr, int X, int Y, int Width, int Height,
        DateTimeOffset CapturedAt, string? SourceApp, string? WindowTitle, string? SavedPath);

    private readonly List<CaptureResult> _items = [];
    private readonly Dictionary<CaptureResult, string> _files = new(ReferenceEqualityComparer.Instance);
    private readonly SemaphoreSlim _io = new(1, 1);
    private readonly string _folder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Recent");

    private string IndexPath => Path.Combine(_folder, "index.json");

    public IReadOnlyList<CaptureResult> Items => _items;

    public void Add(CaptureResult result)
    {
        _items.Insert(0, result);
        if (_items.Count > Capacity)
            _items.RemoveRange(Capacity, _items.Count - Capacity);
        _ = PersistAsync(result);
    }

    /// <summary>Loads the list saved by a previous run. Missing or damaged files are skipped.</summary>
    public async Task LoadAsync()
    {
        await _io.WaitAsync();
        try
        {
            if (!File.Exists(IndexPath)) return;
            var entries = JsonSerializer.Deserialize(await File.ReadAllTextAsync(IndexPath), CaptaJson.Default.ListEntry) ?? [];
            foreach (var e in entries)
            {
                if (_items.Count >= Capacity) break; // captures taken while loading come first
                try
                {
                    var image = await DecodeAsync(Path.Combine(_folder, e.File), transparent: e.Mode == CaptureMode.Freeform);
                    var bounds = new RectInt32(e.X, e.Y, e.Width, e.Height);
                    var monitor = Monitors.AtPoint(new PointInt32(e.X + e.Width / 2, e.Y + e.Height / 2));
                    var result = new CaptureResult(image, monitor, bounds, e.Mode, e.WasHdr)
                    {
                        CapturedAt = e.CapturedAt,
                        SourceApp = e.SourceApp,
                        WindowTitle = e.WindowTitle,
                        SavedPath = e.SavedPath,
                    };
                    _items.Add(result);
                    _files[result] = e.File;
                }
                catch (Exception ex)
                {
                    Log.Info($"Skipped a recent capture: {ex.Message}");
                }
            }
            Log.Info($"Loaded {_items.Count} recent captures");
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't load recent captures", ex);
        }
        finally
        {
            _io.Release();
        }
    }

    /// <summary>Re-writes the index (e.g. after a capture is saved and gains a path).</summary>
    public void Update() => _ = PersistAsync(null);

    private async Task PersistAsync(CaptureResult? added)
    {
        await _io.WaitAsync();
        try
        {
            Directory.CreateDirectory(_folder);
            if (added is not null && IsListed(added))
            {
                var name = $"{added.CapturedAt.UtcTicks}.png";
                using (var png = await ImageExport.EncodePngAsync(added.Image))
                await using (var file = File.Create(Path.Combine(_folder, name)))
                    await png.AsStreamForRead().CopyToAsync(file);
                _files[added] = name;
            }

            var entries = _items.Where(_files.ContainsKey).Select(r => new Entry(
                _files[r], r.Mode, r.WasHdr, r.ScreenBounds.X, r.ScreenBounds.Y, r.ScreenBounds.Width, r.ScreenBounds.Height,
                r.CapturedAt, r.SourceApp, r.WindowTitle, r.SavedPath)).ToList();
            await File.WriteAllTextAsync(IndexPath, JsonSerializer.Serialize(entries, CaptaJson.Default.ListEntry));

            // Drop images that fell off the list.
            var keep = entries.Select(e => e.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stale in _files.Where(kv => !IsListed(kv.Key)).Select(kv => kv.Key).ToList())
                _files.Remove(stale);
            foreach (var path in Directory.EnumerateFiles(_folder, "*.png"))
                if (!keep.Contains(Path.GetFileName(path)))
                    File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't save recent captures", ex);
        }
        finally
        {
            _io.Release();
        }
    }

    private bool IsListed(CaptureResult r) => _items.Any(i => ReferenceEquals(i, r));

    private static async Task<CapturedImage> DecodeAsync(string path, bool transparent)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyToBuffer(pixels.AsBuffer());
        return new CapturedImage(bitmap.PixelWidth, bitmap.PixelHeight, pixels) { HasTransparency = transparent };
    }
}
