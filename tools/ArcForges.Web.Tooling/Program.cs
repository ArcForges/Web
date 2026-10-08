// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Web.Site;

namespace ArcForges.Web.Tooling;

/// <summary>
/// Build tooling entry point. The TypeScript tooling ports here in WEB.40 step 2; only the ported commands run, and every
/// other command is refused with exit code 2.
/// </summary>
public static class Program
{
    private const string SiteBuildUsage = "usage: site build --out <new-directory> [--source-ref <40-hex lower-case commit>]";

    /// <summary>Runs a ported command. Ported: <c>site build</c>, which writes the static public Site to a new directory.</summary>
    public static async Task<int> Main(string[] args)
    {
        if (args is ["site", "build", .. var options])
            return await SiteBuildAsync(options).ConfigureAwait(false);

        Console.Error.WriteLine("ArcForges.Web.Tooling: unknown or unported command. Ported commands: site build.");
        return 2;
    }

    /// <summary>Builds the static Site and writes every file to a directory that must not exist yet.</summary>
    private static async Task<int> SiteBuildAsync(string[] options)
    {
        string? outDirectory = null;
        string? sourceRef = null;
        for (var index = 0; index < options.Length; index++)
        {
            if (options[index] == "--out" && index + 1 < options.Length && outDirectory is null)
                outDirectory = options[++index];
            else if (options[index] == "--source-ref" && index + 1 < options.Length && sourceRef is null)
                sourceRef = options[++index];
            else
            {
                Console.Error.WriteLine(SiteBuildUsage);
                return 2;
            }
        }

        if (string.IsNullOrWhiteSpace(outDirectory))
        {
            Console.Error.WriteLine(SiteBuildUsage);
            return 2;
        }

        // Fail closed: a stale file from an earlier build must never be published by a later one.
        var root = Path.GetFullPath(outDirectory);
        if (Directory.Exists(root) || File.Exists(root))
        {
            Console.Error.WriteLine("Refusing to write into an existing path: " + root);
            return 2;
        }

        SiteOutput output;
        try
        {
            output = await SiteBuilder.BuildAsync(new SiteOptions { SourceRef = sourceRef }).ConfigureAwait(false);
        }
        catch (ArgumentException error)
        {
            Console.Error.WriteLine(error.Message);
            return 2;
        }

        Directory.CreateDirectory(root);
        foreach (var file in output.Files)
        {
            var target = Path.GetFullPath(Path.Combine(root, file.Path));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("Site path escapes the output directory: " + file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, file.Content).ConfigureAwait(false);
        }

        Console.WriteLine($"Wrote {output.Files.Count} Site files to {root}");
        return 0;
    }
}
