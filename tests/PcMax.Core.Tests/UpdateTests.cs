using PcMax.Core.Updates;
using Xunit;

namespace PcMax.Core.Tests;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v0.10.0", 0, 10, 0)]
    [InlineData("  v2.0.15 ", 2, 0, 15)]
    [InlineData("999999.0.1", 999999, 0, 1)]
    [InlineData("1.2.3\n", 1, 2, 3)] // surrounding whitespace is trimmed
    public void Parses_plain_versions(string text, int major, int minor, int patch)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var v));
        Assert.Equal(new ReleaseVersion(major, minor, patch), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3-beta")]
    [InlineData("1.2.3+build")]
    [InlineData("V1.2.3")]
    [InlineData("vv1.2.3")]
    [InlineData("1234567.0.0")]
    [InlineData("-1.2.3")]
    [InlineData("1.2.3/../../evil")]
    [InlineData("１.２.３")] // full-width digits
    public void Rejects_anything_else(string? text)
    {
        Assert.False(ReleaseVersion.TryParse(text, out var v));
        Assert.Equal(default, v);
    }

    [Fact]
    public void Compares_numerically_not_as_text()
    {
        ReleaseVersion.TryParse("0.10.0", out var a);
        ReleaseVersion.TryParse("0.9.9", out var b);
        Assert.True(a > b);
        Assert.True(b < a);
        Assert.True(a >= a && a <= a);
        Assert.True(new ReleaseVersion(2, 0, 0) > new ReleaseVersion(1, 99, 99));
        Assert.True(new ReleaseVersion(1, 2, 4) > new ReleaseVersion(1, 2, 3));
        Assert.Equal(0, new ReleaseVersion(1, 2, 3).CompareTo(new ReleaseVersion(1, 2, 3)));
    }

    [Fact]
    public void ToString_has_no_prefix()
    {
        ReleaseVersion.TryParse("v3.4.5", out var v);
        Assert.Equal("3.4.5", v.ToString());
    }
}

public class ReleaseAssetsTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Asset_names_follow_the_release_workflow()
    {
        var v = new ReleaseVersion(1, 2, 3);
        Assert.Equal("PcMax-Setup-1.2.3.exe", ReleaseAssets.InstallerName(v));
        Assert.Equal("PcMax-Setup-1.2.3.exe.sha256", ReleaseAssets.ChecksumName(v));
    }

    [Theory]
    [InlineData(Hash)]
    [InlineData(Hash + "  PcMax-Setup-1.2.3.exe")]
    [InlineData(Hash + " *PcMax-Setup-1.2.3.exe")]
    [InlineData(Hash + "  PcMax-Setup-1.2.3.exe\n")]
    [InlineData(Hash + "  PcMax-Setup-1.2.3.exe\r\n")]
    [InlineData("\n" + Hash + "  PcMax-Setup-1.2.3.exe")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF  PcMax-Setup-1.2.3.exe")]
    public void Parses_sha256sum_format(string content) =>
        Assert.Equal(Hash, ReleaseAssets.ParseChecksum(content, "PcMax-Setup-1.2.3.exe"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a hash")]
    [InlineData("0123456789abcdef  PcMax-Setup-1.2.3.exe")]                        // too short
    [InlineData(Hash + "0  PcMax-Setup-1.2.3.exe")]                                // too long
    [InlineData(Hash + "  PcMax-Setup-9.9.9.exe")]                                 // other file
    [InlineData(Hash + "  evil.exe")]
    [InlineData("zz23456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")] // not hex
    public void Rejects_bad_checksums(string? content) =>
        Assert.Null(ReleaseAssets.ParseChecksum(content, "PcMax-Setup-1.2.3.exe"));

    [Fact]
    public void Rejects_huge_checksum_files() =>
        Assert.Null(ReleaseAssets.ParseChecksum(Hash + new string(' ', 5000), "PcMax-Setup-1.2.3.exe"));
}

public class ReleaseFeedTests
{
    private static readonly ReleaseVersion Current = new(0, 1, 0);

    private static string Json(string tag = "v0.2.0", bool draft = false, bool pre = false, string assets = """
        [{"name":"PcMax-Setup-0.2.0.exe","size":1000,"browser_download_url":"https://evil.example/x.exe"},
         {"name":"PcMax-Setup-0.2.0.exe.sha256","size":90}]
        """) =>
        $$"""{"tag_name":"{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(pre ? "true" : "false")}},"assets":{{assets}}}""";

    [Fact]
    public void Picks_installer_and_builds_urls_itself()
    {
        var c = ReleaseFeed.TryParseLatest(Json(), "owner", "repo", Current, out _);
        Assert.NotNull(c);
        Assert.Equal(new ReleaseVersion(0, 2, 0), c!.Version);
        Assert.Equal("PcMax-Setup-0.2.0.exe", c.InstallerName);
        // Never the URL from the JSON.
        Assert.Equal("https://github.com/owner/repo/releases/download/v0.2.0/PcMax-Setup-0.2.0.exe", c.InstallerUrl.AbsoluteUri);
        Assert.Equal("https://github.com/owner/repo/releases/download/v0.2.0/PcMax-Setup-0.2.0.exe.sha256", c.ChecksumUrl.AbsoluteUri);
        Assert.Equal(1000, c.InstallerSize);
    }

    [Fact]
    public void Same_or_older_version_is_not_an_update()
    {
        Assert.Null(ReleaseFeed.TryParseLatest(Json(), "o", "r", new ReleaseVersion(0, 2, 0), out _));
        Assert.Null(ReleaseFeed.TryParseLatest(Json(), "o", "r", new ReleaseVersion(1, 0, 0), out _));
    }

    [Fact]
    public void Drafts_and_prereleases_are_ignored()
    {
        Assert.Null(ReleaseFeed.TryParseLatest(Json(draft: true), "o", "r", Current, out _));
        Assert.Null(ReleaseFeed.TryParseLatest(Json(pre: true), "o", "r", Current, out _));
    }

    [Theory]
    [InlineData("v0.2.0/../../x")]
    [InlineData("v0.2.0-rc1")]
    [InlineData("latest")]
    [InlineData("")]
    public void Bad_tags_are_rejected(string tag) =>
        Assert.Null(ReleaseFeed.TryParseLatest(Json(tag: tag), "o", "r", Current, out _));

    [Fact]
    public void Both_assets_must_exist_with_exact_names()
    {
        Assert.Null(ReleaseFeed.TryParseLatest(Json(assets: """[{"name":"PcMax-Setup-0.2.0.exe","size":1}]"""), "o", "r", Current, out _));
        Assert.Null(ReleaseFeed.TryParseLatest(Json(assets: """[{"name":"PcMax-Setup-0.2.0.exe.sha256","size":1}]"""), "o", "r", Current, out _));
        Assert.Null(ReleaseFeed.TryParseLatest(Json(assets: """[{"name":"pcmax-setup-0.2.0.EXE","size":1},{"name":"PcMax-Setup-0.2.0.exe.sha256"}]"""), "o", "r", Current, out _));
        Assert.Null(ReleaseFeed.TryParseLatest(Json(assets: "null"), "o", "r", Current, out _));
        Assert.Null(ReleaseFeed.TryParseLatest(Json(assets: "[null, 3, \"x\"]"), "o", "r", Current, out _));
    }

    [Fact]
    public void Oversized_installer_is_rejected()
    {
        var big = ReleaseFeed.MaxInstallerBytes + 1;
        var json = Json(assets: $$"""[{"name":"PcMax-Setup-0.2.0.exe","size":{{big}}},{"name":"PcMax-Setup-0.2.0.exe.sha256"}]""");
        Assert.Null(ReleaseFeed.TryParseLatest(json, "o", "r", Current, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"tag_name\":5}")]
    public void Garbage_is_rejected(string? json) =>
        Assert.Null(ReleaseFeed.TryParseLatest(json, "o", "r", Current, out _));
}
