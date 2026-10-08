// SPDX-License-Identifier: AGPL-3.0-only
using System.IO.Compression;
using System.Text.Json.Nodes;
using ArcForges.Web.Tooling.Candidate;

namespace ArcForges.Web.Tooling.Profiles;

/// <summary>The measured size facts of one published Blazor WebAssembly application.</summary>
/// <remarks>
/// Every gzip figure is the length of the file compressed in-process with <see cref="CompressionLevel.SmallestSize"/>, so
/// the measure does not depend on the precompressed siblings the SDK writes. The initial set is every published file
/// except those siblings: the shell page is the only page, and the framework and application resources are loaded at
/// startup (the publish has no lazy assemblies).
/// </remarks>
public sealed record ProfileMeasure(
    int InitialRequests,
    long HtmlBytes,
    long InitialCssGzip,
    long InitialJsGzip,
    long InitialWasmGzip,
    long InitialDataGzip,
    long InitialOtherGzip,
    long TotalGzip)
{
    /// <summary>The metric names, in the budget file's order.</summary>
    public static readonly string[] Metrics =
    [
        "initialRequests",
        "htmlBytes",
        "initialCssGzip",
        "initialJsGzip",
        "initialWasmGzip",
        "initialDataGzip",
        "initialOtherGzip",
        "totalGzip",
    ];

    /// <summary>The value of a named metric.</summary>
    public long Value(string metric) => metric switch
    {
        "initialRequests" => InitialRequests,
        "htmlBytes" => HtmlBytes,
        "initialCssGzip" => InitialCssGzip,
        "initialJsGzip" => InitialJsGzip,
        "initialWasmGzip" => InitialWasmGzip,
        "initialDataGzip" => InitialDataGzip,
        "initialOtherGzip" => InitialOtherGzip,
        "totalGzip" => TotalGzip,
        _ => throw new InvalidOperationException("Unknown budget metric: " + metric),
    };
}

/// <summary>The reviewed budget file of the Blazor profiles (eng/policy/profile-budgets.json).</summary>
public sealed record ProfileBudgets(int RegressionPercent, IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> Profiles)
{
    /// <summary>The profile routes the budget file must name exactly (the bundle's profile paths).</summary>
    public static readonly string[] RequiredProfiles = ProfileBundle.Profiles;

    /// <summary>Parses and validates the budget document. Any missing, extra or non-integer field fails closed.</summary>
    public static ProfileBudgets Parse(byte[] json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("The budget file is not a JSON object.");
        RequireKeys(root, "schema regressionPercent profiles interactionBudgets", "The budget file keys changed.");
        Require(root["schema"]?.GetValue<int>() == 1, "Unsupported budget schema.");
        var regression = root["regressionPercent"]?.GetValue<int>() ?? throw new InvalidOperationException("No regressionPercent.");
        Require(regression is >= 0 and <= 100, "regressionPercent must be between 0 and 100.");
        var profiles = root["profiles"]?.AsObject() ?? throw new InvalidOperationException("No profiles.");
        Require(profiles.Select(pair => pair.Key).OrderBy(key => key, StringComparer.Ordinal).SequenceEqual(RequiredProfiles.OrderBy(key => key, StringComparer.Ordinal)),
            "The budget file must name exactly the bundle profiles.");
        var result = new Dictionary<string, IReadOnlyDictionary<string, long>>(StringComparer.Ordinal);
        foreach (var (profile, node) in profiles)
        {
            var metrics = node?.AsObject() ?? throw new InvalidOperationException("No budget object for " + profile);
            Require(metrics.Select(pair => pair.Key).OrderBy(key => key, StringComparer.Ordinal).SequenceEqual(ProfileMeasure.Metrics.OrderBy(key => key, StringComparer.Ordinal)),
                "The budget metrics of " + profile + " changed.");
            var values = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var metric in ProfileMeasure.Metrics)
            {
                var value = metrics[metric]?.GetValue<long>() ?? throw new InvalidOperationException("No baseline for " + profile + "." + metric);
                Require(value > 0, "A baseline must be a positive measured value: " + profile + "." + metric);
                values[metric] = value;
            }
            result[profile] = values;
        }
        return new ProfileBudgets(regression, result);
    }

    /// <summary>The largest value a metric may reach: its baseline plus the allowed regression, rounded down.</summary>
    public static long Limit(long baseline, int regressionPercent) => baseline + baseline * regressionPercent / 100;

    private static void RequireKeys(JsonObject node, string expected, string message)
    {
        var names = expected.Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderBy(key => key, StringComparer.Ordinal);
        Require(node.Select(pair => pair.Key).OrderBy(key => key, StringComparer.Ordinal).SequenceEqual(names), message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

/// <summary>Measures the published application and enforces the reviewed budgets against it.</summary>
public static class ProfileBudget
{
    /// <summary>Measures a publish directory (the output of <c>dotnet publish</c> of ArcForges.Web.App).</summary>
    public static ProfileMeasure Measure(string publishDirectory)
    {
        var wwwroot = Path.Combine(Path.GetFullPath(publishDirectory), "wwwroot");
        if (!Directory.Exists(wwwroot))
            throw new InvalidOperationException("The publish output has no wwwroot directory.");
        var tree = CandidateCore.ReadTree(wwwroot);
        if (!tree.TryGetValue(ProfileBundle.ShellPath, out var shell))
            throw new InvalidOperationException("The publish output has no application shell.");

        long css = 0, js = 0, wasm = 0, data = 0, other = 0;
        var requests = 0;
        foreach (var (path, bytes) in tree)
        {
            if (path.EndsWith(".br", StringComparison.Ordinal) || path.EndsWith(".gz", StringComparison.Ordinal))
                continue;
            requests++;
            if (path == ProfileBundle.ShellPath)
                continue;
            var gzip = GzipLength(bytes);
            if (path.EndsWith(".css", StringComparison.Ordinal))
                css += gzip;
            else if (path.StartsWith("_framework/", StringComparison.Ordinal) && path.EndsWith(".js", StringComparison.Ordinal))
                js += gzip;
            else if (path.StartsWith("_framework/", StringComparison.Ordinal) && path.EndsWith(".wasm", StringComparison.Ordinal))
                wasm += gzip;
            else if (path.StartsWith("_framework/", StringComparison.Ordinal) && path.EndsWith(".dat", StringComparison.Ordinal))
                data += gzip;
            else
                other += gzip;
        }
        return new ProfileMeasure(
            requests,
            shell.Length,
            css,
            js,
            wasm,
            data,
            other,
            css + js + wasm + data + other);
    }

    /// <summary>
    /// Enforces the budgets: every profile metric must stay at or below its baseline plus the allowed regression. The
    /// report lists every metric; the first failing one raises the verification error.
    /// </summary>
    public static string Check(ProfileMeasure measure, ProfileBudgets budgets)
    {
        var report = new System.Text.StringBuilder();
        foreach (var profile in budgets.Profiles.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            var baselines = budgets.Profiles[profile];
            foreach (var metric in ProfileMeasure.Metrics)
            {
                var baseline = baselines[metric];
                var limit = ProfileBudgets.Limit(baseline, budgets.RegressionPercent);
                var value = measure.Value(metric);
                report.Append($"{profile}.{metric}: {value} (baseline {baseline}, limit {limit})\n");
                if (value > limit)
                    throw new InvalidOperationException(
                        $"Profile budget exceeded: {profile}.{metric} is {value}, limit {limit} (baseline {baseline}, {budgets.RegressionPercent}% regression allowed).");
            }
        }
        return report.ToString();
    }

    /// <summary>The gzip length of the bytes at the fixed compression level.</summary>
    public static long GzipLength(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(bytes);
        return output.Length;
    }
}
