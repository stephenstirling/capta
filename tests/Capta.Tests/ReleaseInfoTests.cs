using Capta.Services;

namespace Capta.Tests;

public class ReleaseInfoTests
{
    private static string Json(string tag = "v1.1.2", string url = "https://github.com/stephenstirling/capta/releases/tag/v1.1.2",
        bool draft = false, bool prerelease = false) =>
        $$"""{"tag_name":"{{tag}}","html_url":"{{url}}","draft":{{draft.ToString().ToLowerInvariant()}},"prerelease":{{prerelease.ToString().ToLowerInvariant()}},"name":"Capta"}""";

    [Fact]
    public void ReadsTheLatestRelease()
    {
        var release = ReleaseInfo.Parse(Json());
        Assert.NotNull(release);
        Assert.Equal(new Version(1, 1, 2, 0), release!.Version);
        Assert.Equal("v1.1.2", release.Tag);
        Assert.Equal("https://github.com/stephenstirling/capta/releases/tag/v1.1.2", release.Url);
        Assert.Equal("Version 1.1.2", release.Title);
    }

    [Theory]
    [InlineData("v1.1.2", 1, 1, 2, 0)]
    [InlineData("1.1.2", 1, 1, 2, 0)]
    [InlineData("V2.0", 2, 0, 0, 0)]
    [InlineData("v1.2.3.4", 1, 2, 3, 4)]
    [InlineData(" v1.10.0 ", 1, 10, 0, 0)]
    public void ParsesVersionTags(string tag, int major, int minor, int build, int revision)
    {
        Assert.True(ReleaseInfo.TryParseVersion(tag, out var version));
        Assert.Equal(new Version(major, minor, build, revision), version);
    }

    [Theory]
    [InlineData("v1.2.0-beta")]
    [InlineData("latest")]
    [InlineData("v1")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("v1.x.0")]
    public void RejectsTagsThatAreNotVersions(string? tag) => Assert.False(ReleaseInfo.TryParseVersion(tag, out _));

    [Fact]
    public void IgnoresDraftsAndPreReleases()
    {
        Assert.Null(ReleaseInfo.Parse(Json(draft: true)));
        Assert.Null(ReleaseInfo.Parse(Json(prerelease: true)));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"tag_name":"v1.2.0"}""")]
    [InlineData("""{"html_url":"https://github.com/x/y/releases/tag/v1.2.0"}""")]
    [InlineData("""{"tag_name":"nightly","html_url":"https://github.com/x/y/releases/tag/nightly"}""")]
    public void IgnoresResponsesItCannotUse(string json) => Assert.Null(ReleaseInfo.Parse(json));

    [Theory]
    [InlineData("http://github.com/stephenstirling/capta/releases/tag/v1.1.2")]   // not https
    [InlineData("https://evil.example.com/releases/tag/v1.1.2")]
    [InlineData("https://github.com.evil.example.com/releases/tag/v1.1.2")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("calc.exe")]
    public void OnlyOpensGitHubPages(string url) => Assert.Null(ReleaseInfo.Parse(Json(url: url)));

    [Theory]
    [InlineData("v1.1.1", "1.1.0.0", true)]
    [InlineData("v1.2.0", "1.1.9.0", true)]
    [InlineData("v2.0.0", "1.9.9.9", true)]
    [InlineData("v1.1.0", "1.1.0.0", false)]   // the same version
    [InlineData("v1.1", "1.1.0.0", false)]
    [InlineData("v1.0.9", "1.1.0.0", false)]   // older
    [InlineData("v1.10.0", "1.9.0.0", true)]   // numeric, not alphabetical
    [InlineData("v1.1.0", "1.0.0.0", true)]    // 1.0 sees the 1.1 release
    [InlineData("v1.1.0", "1.0.288.36167", true)] // a dev build stamped 1.0.<day>.<n>
    public void ComparesAgainstTheRunningVersion(string tag, string current, bool newer)
    {
        var release = ReleaseInfo.Parse(Json(tag: tag, url: $"https://github.com/stephenstirling/capta/releases/tag/{tag}"));
        Assert.NotNull(release);
        Assert.Equal(newer, release!.IsNewerThan(Version.Parse(current)));
    }

    [Fact]
    public void TheTitleUsesThreeParts() =>
        Assert.Equal("Version 1.2.0", ReleaseInfo.Parse(Json(tag: "v1.2", url: "https://github.com/a/b/releases/tag/v1.2"))!.Title);

    [Fact]
    public void ChecksAtMostOnceADay()
    {
        var last = new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
        Assert.True(UpdateSchedule.IsDue(null, last));
        Assert.False(UpdateSchedule.IsDue(last, last));
        Assert.False(UpdateSchedule.IsDue(last, last.AddHours(23).AddMinutes(59)));
        Assert.True(UpdateSchedule.IsDue(last, last.AddHours(24)));
        Assert.True(UpdateSchedule.IsDue(last, last.AddDays(30)));
    }

    [Fact]
    public void AClockSetBackCountsAsDue() =>
        Assert.True(UpdateSchedule.IsDue(new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero)));
}
