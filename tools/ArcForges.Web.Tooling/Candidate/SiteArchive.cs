// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.RegularExpressions;
using ArcForges.Web.Site;
using ArcForges.Web.Tooling.Profiles;

namespace ArcForges.Web.Tooling.Candidate;

/// <summary>
/// The Site release asset: the deterministic ustar archive of the C# static Site output, named by the digest of its own
/// bytes (<c>web-site-&lt;sha256&gt;.tar</c>). It is written by the same ustar writer as the profile bundle, so its entries are
/// plain regular files in ordinal path order with fixed metadata, and equal Site output is the same bytes.
/// The archive sits at the root of the sealed candidate, beside the assets directory that Cloudflare deploys, so the seal
/// records its digest and the verifier refuses any other bytes. It is published only by the main-push release, beside
/// <c>web-profiles</c>. It is not part of the deployed assets.
/// </summary>
internal static partial class SiteArchive
{
    /// <summary>The archive file name for a digest.</summary>
    public static string Name(string digest) => "web-site-" + digest + ".tar";

    /// <summary>Writes the Site files, in ordinal path order, as one archive.</summary>
    public static byte[] Build(IReadOnlyList<SiteFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var entries = files
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .Select(file => new TarArchive.Entry(file.Path, file.Content))
            .ToArray();
        return TarArchive.Write(entries);
    }

    /// <summary>The root file name of a Site archive: its name pattern, with the digest of its own bytes.</summary>
    public static bool IsName(string path) => NamePattern().IsMatch(path);

    [GeneratedRegex(@"^web-site-[0-9a-f]{64}\.tar$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
