// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using ArcForges.Web.Tooling.Profiles;
using Xunit;

namespace ArcForges.Web.Tooling.Tests;

/// <summary>
/// The archive of the Blazor profile bundle (the successor of the bundle archive cases of tests/unit/app-bundle.test.ts):
/// deterministic bytes, a digest-named file, an exact read-back, and refusal of unsafe, duplicate, damaged or trailing data.
/// </summary>
public sealed class ProfileBundleArchiveTests
{
    private static TarArchive.Entry Entry(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));

    [Fact]
    public void TheArchiveIsDeterministicAndReadsBackExactlyWhatWasWritten()
    {
        var entries = new[] { Entry("manifest.json", "{}"), Entry("account/index.html", "<html></html>"), Entry("_headers", "/*\n") };

        var first = TarArchive.Write(entries);
        var second = TarArchive.Write(entries);
        var read = TarArchive.Read(first);

        Assert.Equal(first, second);
        Assert.Equal(entries.Select(entry => entry.Path), read.Select(entry => entry.Path));
        Assert.Equal(entries.Select(entry => entry.Bytes), read.Select(entry => entry.Bytes));
    }

    [Fact]
    public void TheBundleNameIsTheDigestOfItsOwnArchive()
    {
        var archive = TarArchive.Write([Entry("manifest.json", "{}")]);
        var digest = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();

        Assert.Equal("web-profiles-" + digest + ".tar", ProfileBundle.BundleName(digest));
        Assert.Matches("^web-profiles-[a-f0-9]{64}\\.tar$", ProfileBundle.BundleName(digest));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/absolute.txt")]
    public void AnUnsafePathIsRefusedWhenWriting(string path)
    {
        Assert.Throws<InvalidOperationException>(() => TarArchive.Write([Entry(path, "x")]));
    }

    [Fact]
    public void ADuplicatePathIsRefusedWhenWriting()
    {
        Assert.Throws<InvalidOperationException>(() => TarArchive.Write([Entry("a.txt", "one"), Entry("a.txt", "two")]));
    }

    [Fact]
    public void DataAfterTheTerminatorIsRefusedWhenReading()
    {
        var archive = TarArchive.Write([Entry("manifest.json", "{}")]);
        var trailing = archive.Concat(new byte[] { 0x41 }).ToArray();

        Assert.Throws<InvalidOperationException>(() => TarArchive.Read(trailing));
    }

    [Fact]
    public void ATruncatedArchiveIsRefusedWhenReading()
    {
        var archive = TarArchive.Write([Entry("manifest.json", "{\"schema\":1}")]);
        var truncated = archive[..(archive.Length / 2)];

        Assert.ThrowsAny<InvalidOperationException>(() => TarArchive.Read(truncated));
    }
}
