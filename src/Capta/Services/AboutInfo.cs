using Windows.ApplicationModel;

namespace Capta.Services;

/// <summary>What the About section of Startup &amp; shortcuts shows.</summary>
public static class AboutInfo
{
    public const string Copyright = "\u00A9 Stephen Stirling";
    public const string License = "Apache License 2.0";

    /// <summary>Whether Capta is running from the Microsoft Store, which updates it. False for other builds.</summary>
    public static bool IsStoreBuild
    {
        get
        {
            try
            {
                return Package.Current.SignatureKind == PackageSignatureKind.Store;
            }
            catch (InvalidOperationException)
            {
                return false; // no package identity: not installed from the Store
            }
        }
    }

    /// <summary>The running version: the package version (Package.appxmanifest), or the assembly's without one.</summary>
    public static Version CurrentVersion
    {
        get
        {
            try
            {
                var v = Package.Current.Id.Version;
                return new Version(v.Major, v.Minor, v.Build, v.Revision);
            }
            catch (InvalidOperationException)
            {
                return typeof(AboutInfo).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
            }
        }
    }

    /// <summary>The running version as shown to people, e.g. "1.1.0".</summary>
    public static string Version => CurrentVersion.ToString(3);
}
