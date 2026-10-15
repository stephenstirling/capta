using Windows.System;

namespace Capta.Services;

/// <summary>Author links shown in the app.</summary>
public static class Links
{
    public static readonly Uri GitHub = new("https://github.com/stephenstirling");
    public static readonly Uri Website = new("https://github.com/stephenstirling/capta");
    public static readonly Uri PrivacyPolicy = new("https://github.com/stephenstirling/capta/blob/main/docs/privacy.md");
    public static readonly Uri BuyMeACoffee = new("https://buymeacoffee.com/stephenstirling");
    /// <summary>
    /// Ocula's Microsoft Store page, e.g. <c>ms-windows-store://pdp/?productid=9XXXXXXXXXXX</c>. Null until
    /// Ocula has a listing; while it's null a missing Ocula just shows a disabled button and tooltip
    /// instead of "Get Ocula".
    /// </summary>
    public static Uri? OculaStore => null;

    public static readonly Uri Releases = new("https://github.com/stephenstirling/capta/releases/latest");
    public static readonly Uri License = new("https://www.apache.org/licenses/LICENSE-2.0");

    public static Task OpenAsync(Uri uri) => Launcher.LaunchUriAsync(uri).AsTask();
}
