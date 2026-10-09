// SPDX-License-Identifier: AGPL-3.0-only
namespace ArcForges.Web.Operations.Tests;

/// <summary>Locates the repository root from the test output directory.</summary>
public static class RepositoryRoot
{
    /// <summary>Walks up from the test assembly until the repository marker files are found.</summary>
    public static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "win.slnx"))
                && File.Exists(Path.Combine(directory.FullName, "global.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("The repository root with win.slnx and global.json was not found.");
    }
}
