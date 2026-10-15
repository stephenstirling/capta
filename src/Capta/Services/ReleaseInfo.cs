using System.Text.Json;
using System.Text.RegularExpressions;

namespace Capta.Services;

/// <summary>A published Capta release, as read from GitHub Releases.</summary>
/// <param name="Tag">The release tag, e.g. "v1.1.2".</param>
/// <param name="Url">The release page (notes and downloads), opened in the browser.</param>
public sealed partial record ReleaseInfo(Version Version, string Tag, string Url)
{
    /// <summary>"Version 1.1.2"</summary>
    public string Title => $"Version {Version.Major}.{Version.Minor}.{Math.Max(Version.Build, 0)}";

    /// <summary>
    /// The release in GitHub's "latest release" JSON, or null if it isn't a usable stable release: a
    /// draft or pre-release, a tag that isn't a version, or a page that isn't on github.com.
    /// </summary>
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Flag(root, "draft") || Flag(root, "prerelease")) return null;
            if (!root.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { } tagName) return null;
            if (!root.TryGetProperty("html_url", out var url) || url.GetString() is not { } page) return null;
            if (!TryParseVersion(tagName, out var version)) return null;
            // The page is opened in the browser: only ever a github.com address.
            if (!Uri.TryCreate(page, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                return null;
            return new ReleaseInfo(version, tagName, uri.AbsoluteUri);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"^[vV]?(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?$")]
    private static partial Regex TagPattern();

    /// <summary>"v1.1.2", "1.1" and "1.1.2.0" are versions; "v1.2.0-beta" and "latest" aren't.</summary>
    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (tag is null || TagPattern().Match(tag.Trim()) is not { Success: true } m) return false;
        if (!int.TryParse(m.Groups[1].Value, out var major) || !int.TryParse(m.Groups[2].Value, out var minor)) return false;
        var build = m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out var b) ? b : 0;
        var revision = m.Groups[4].Success && int.TryParse(m.Groups[4].Value, out var r) ? r : 0;
        version = new Version(major, minor, build, revision);
        return true;
    }

    /// <summary>Whether this release is newer than the running version (1.1 and 1.1.0.0 are the same).</summary>
    public bool IsNewerThan(Version current) => Normalise(Version) > Normalise(current);

    private static Version Normalise(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}

/// <summary>When the background update check may run.</summary>
public static class UpdateSchedule
{
    /// <summary>At most one background check in this long.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public static bool IsDue(DateTimeOffset? lastCheck, DateTimeOffset now) =>
        lastCheck is not { } last || now - last >= Interval || now < last; // a clock set back counts as due
}
