// SPDX-License-Identifier: AGPL-3.0-only
namespace ArcForges.Web.Site;

/// <summary>Reads the embedded static files of the Site (the stylesheet, the 404 page, the favicon and robots.txt).</summary>
internal static class StaticAssets
{
    private const string Prefix = "ArcForges.Web.Site.Assets.";

    /// <summary>Returns the exact bytes of an embedded asset.</summary>
    /// <exception cref="InvalidOperationException">The asset is not embedded in this assembly.</exception>
    public static byte[] Bytes(string name)
    {
        using var stream = typeof(StaticAssets).Assembly.GetManifestResourceStream(Prefix + name)
            ?? throw new InvalidOperationException("The static asset is not embedded: " + name);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
