using System.Diagnostics;

namespace Capta.Services;

/// <summary>Tiny append-only diagnostic log at %LOCALAPPDATA%\Packages\&lt;pfn&gt;\LocalState\capta.log.</summary>
public static class Log
{
    private static readonly object s_lock = new();
    private static readonly string s_path = ResolvePath();

    public static string FilePath => s_path;

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        Debug.WriteLine(line);
        try
        {
            lock (s_lock)
            {
                // Keep the file small: start over past 1 MB.
                var info = new FileInfo(s_path);
                if (info.Exists && info.Length > 1_000_000) info.Delete();
                File.AppendAllText(s_path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    private static string ResolvePath()
    {
        try
        {
            return Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "capta.log");
        }
        catch
        {
            // No package identity (e.g. run outside the MSIX).
            return Path.Combine(Path.GetTempPath(), "capta.log");
        }
    }
}
