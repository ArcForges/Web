// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json.Nodes;

namespace ArcForges.Web.Policy.Tests;

/// <summary>Compares the restored NuGet closure with the admitted closure in eng/policy (WEB.40 successor check).</summary>
public static class NuGetAdmission
{
    private static readonly string[] PermissiveLicences = ["MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "ISC"];

    /// <summary>Returns every refusal reason; an empty list means the closure is admitted.</summary>
    public static IReadOnlyList<string> Violations(JsonObject admitted, IReadOnlyDictionary<string, string> restored)
    {
        var violations = new List<string>();
        foreach (var key in restored.Keys.Where(key => !admitted.ContainsKey(key)))
            violations.Add("Unadmitted NuGet package: " + key);
        foreach (var key in admitted.Select(entry => entry.Key).Where(key => !restored.ContainsKey(key)))
            violations.Add("Admitted package missing from the restored closure: " + key);
        foreach (var (key, row) in admitted.Select(entry => (entry.Key, entry.Value!.AsObject())))
        {
            if (restored.TryGetValue(key, out var content) && row["contentHash"]?.GetValue<string>() != content)
                violations.Add("Content hash differs from the admitted record: " + key);
            var licence = row["licence"]?.GetValue<string>() ?? string.Empty;
            var firstParty = key.StartsWith("arcforges.", StringComparison.Ordinal);
            if (!PermissiveLicences.Contains(licence, StringComparer.Ordinal)
                && !(firstParty && licence == "AGPL-3.0-only"))
                violations.Add("Forbidden or unreviewed licence " + licence + ": " + key);
        }
        return violations;
    }
}
