using System.Text.Json;

namespace Capta.Services;

/// <summary>
/// Integra (github.com/stephenstirling/integra) installs and updates the Stirling apps from their GitHub releases and
/// keeps a list of what it installed in %LOCALAPPDATA%\Integra\installed.json. When Capta is on that list Integra does
/// the updating, so Capta doesn't also check GitHub and show its own update card.
/// </summary>
public static class IntegraManagement
{
    private const string AppId = "capta";

    /// <summary>The list Integra keeps. Packaged Capta can read it: the file is outside the package, and read-only.</summary>
    public static string ListPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Integra", "installed.json");

    /// <summary>True when Integra's own list says it installed Capta. Any problem reading it counts as "not managed".</summary>
    public static bool IsManagedByIntegra()
    {
        try
        {
            return File.Exists(ListPath) && Lists(File.ReadAllText(ListPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>Whether Integra's installed.json (a list of records with an "Id") has a record for Capta that Integra made.</summary>
    public static bool Lists(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
            foreach (var record in document.RootElement.EnumerateArray())
            {
                if (record.ValueKind != JsonValueKind.Object) continue;
                string? id = null, origin = null;
                foreach (var property in record.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.String) continue;
                    if (property.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
                        id = property.Value.GetString();
                    else if (property.Name.Equals("origin", StringComparison.OrdinalIgnoreCase))
                        origin = property.Value.GetString();
                }
                // Integra also notes an app it merely found installed ("detected"); only its own installs count.
                if (string.Equals(id, AppId, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(origin, "detected", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
