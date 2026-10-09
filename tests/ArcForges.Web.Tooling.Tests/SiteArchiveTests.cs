// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using ArcForges.Web.Site;
using ArcForges.Web.Tooling.Candidate;
using ArcForges.Web.Tooling.Profiles;
using Xunit;

namespace ArcForges.Web.Tooling.Tests;

/// <summary>
/// The Site release asset (<c>web-site-&lt;sha256&gt;.tar</c>): two builds of the same Site are the same bytes, the name is the
/// digest of those bytes, and the archive holds exactly the Site's own files in ordinal path order. The candidate seal and
/// the refusal of a tampered archive are covered by tests/provenance/csharp-candidate.test.ts on a sealed candidate.
/// </summary>
public sealed class SiteArchiveTests
{
    private const string SourceRef = "0123456789abcdef0123456789abcdef01234567";

    private static async Task<SiteOutput> BuildSiteAsync() =>
        await SiteBuilder.BuildAsync(new SiteOptions { SourceRef = SourceRef });

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [Fact]
    public async Task TwoBuildsOfTheSiteGiveTheSameArchiveBytes()
    {
        var first = SiteArchive.Build((await BuildSiteAsync()).Files);
        var second = SiteArchive.Build((await BuildSiteAsync()).Files);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task TheArchiveIsNamedByTheDigestOfItsOwnBytes()
    {
        var archive = SiteArchive.Build((await BuildSiteAsync()).Files);
        var name = SiteArchive.Name(Digest(archive));

        Assert.Matches("^web-site-[a-f0-9]{64}\\.tar$", name);
        Assert.True(SiteArchive.IsName(name));
        Assert.False(SiteArchive.IsName("web-site-" + new string('a', 64) + ".tar.gz"));
        Assert.False(SiteArchive.IsName("web-profiles-" + Digest(archive) + ".tar"));
    }

    [Fact]
    public async Task TheArchiveHoldsEverySiteFileInOrderAndNothingElse()
    {
        var site = await BuildSiteAsync();
        var expected = site.Files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();

        var entries = TarArchive.Read(SiteArchive.Build(site.Files));

        Assert.Equal(expected.Select(file => file.Path), entries.Select(entry => entry.Path));
        Assert.Equal(expected.Select(file => file.Content), entries.Select(entry => entry.Bytes));
    }

    [Fact]
    public async Task AChangedSiteFileChangesTheArchiveAndItsName()
    {
        var site = await BuildSiteAsync();
        var original = SiteArchive.Build(site.Files);
        var changed = site.Files
            .Select(file => file.Path == "robots.txt" ? file with { Content = [.. file.Content, (byte)'#'] } : file)
            .ToArray();

        var other = SiteArchive.Build(changed);

        Assert.NotEqual(original, other);
        Assert.NotEqual(SiteArchive.Name(Digest(original)), SiteArchive.Name(Digest(other)));
    }
}
