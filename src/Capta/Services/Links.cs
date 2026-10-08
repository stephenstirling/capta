using Windows.System;

namespace Capta.Services;

/// <summary>Author links shown in the app.</summary>
public static class Links
{
    public static readonly Uri GitHub = new("https://github.com/stephenstirling");
    public static readonly Uri BuyMeACoffee = new("https://buymeacoffee.com/stephenstirling");

    public static Task OpenAsync(Uri uri) => Launcher.LaunchUriAsync(uri).AsTask();
}
