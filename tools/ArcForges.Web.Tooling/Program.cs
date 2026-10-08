// SPDX-License-Identifier: AGPL-3.0-only
namespace ArcForges.Web.Tooling;

/// <summary>Build tooling entry point. The TypeScript tooling ports here in WEB.40 step 2.</summary>
public static class Program
{
    /// <summary>Refuses every command until the tooling port lands; no build behaviour exists yet.</summary>
    public static int Main(string[] args)
    {
        Console.Error.WriteLine("ArcForges.Web.Tooling: no commands are ported yet (WEB.40 step 2).");
        return 2;
    }
}
