using System.Net;
using System.Net.Http.Headers;

namespace Capta.Services;

/// <summary>
/// Reads the latest release from GitHub Releases (the public API), for the website build's "Check for
/// updates". It is Capta's only network call: one GET, no account, no cookies, nothing sent about the
/// user. It never downloads or installs anything; the release page is opened in the browser.
/// </summary>
public static class UpdateChecker
{
    private static readonly Uri LatestRelease = new("https://api.github.com/repos/stephenstirling/capta/releases/latest");

    private static readonly Lazy<HttpClient> s_http = new(() =>
    {
        var client = new HttpClient(new SocketsHttpHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Capta", AboutInfo.Version)); // GitHub requires one
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    });

    /// <summary>The latest stable release, or null if there is none (or none is public yet). Throws if GitHub can't be reached.</summary>
    public static async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellation = default)
    {
        using var response = await s_http.Value.GetAsync(LatestRelease, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return ReleaseInfo.Parse(await response.Content.ReadAsStringAsync(cancellation));
    }
}
