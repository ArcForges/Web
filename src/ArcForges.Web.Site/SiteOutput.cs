// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;

namespace ArcForges.Web.Site;

/// <summary>Build inputs of the static Site.</summary>
public sealed record SiteOptions
{
    /// <summary>
    /// The 40-hex source commit of the build. When set, the source link points at its commit, as the React build did
    /// from VITE_SOURCE_REF. When null, the link points at the repository.
    /// </summary>
    public string? SourceRef { get; init; }
}

/// <summary>One file of the static Site output, at a path relative to the public root.</summary>
public sealed record SiteFile(string Path, byte[] Content, string ContentType)
{
    /// <summary>The lower-case SHA-256 of the content.</summary>
    public string Sha256 => Convert.ToHexString(SHA256.HashData(Content)).ToLowerInvariant();
}

/// <summary>The complete static Site output: every file, sorted by path, with no timestamp or environment input.</summary>
public sealed record SiteOutput(IReadOnlyList<SiteFile> Files)
{
    /// <summary>Returns the file at <paramref name="path"/>, or null when the Site does not publish it.</summary>
    public SiteFile? Find(string path) => Files.FirstOrDefault(file => string.Equals(file.Path, path, StringComparison.Ordinal));
}
