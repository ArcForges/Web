// SPDX-License-Identifier: AGPL-3.0-only
using System.IO.Compression;
using System.Text;
using ArcForges.Web.Tooling.Profiles;
using Xunit;

namespace ArcForges.Web.Tooling.Tests;

/// <summary>
/// The Blazor profile budgets (eng/policy/profile-budgets.json). They replace the React budgets of apps/app. Each case has
/// a passing and a failing example; the one-for-one successor of tests/unit/app-proof.test.ts budget cases.
/// </summary>
public sealed class ProfileBudgetTests
{
    private static readonly string BudgetPath = Path.Combine(RepositoryRoot.Find(), "eng", "policy", "profile-budgets.json");

    private static string Document(string profiles = "", int regression = 10, string extra = "")
    {
        var metrics = string.Join(", ", ProfileMeasure.Metrics.Select(metric => $"\"{metric}\": 1000"));
        return "{\"schema\": 1, \"regressionPercent\": " + regression + ", \"profiles\": {"
            + (profiles.Length > 0 ? profiles : $"\"account\": {{{metrics}}}, \"chat\": {{{metrics}}}")
            + "}, \"interactionBudgets\": {\"status\": \"re-baseline-pending\"}" + extra + "}";
    }

    private static ProfileMeasure Measure(long value) => new(
        (int)value,
        value,
        value,
        value,
        value,
        value,
        value,
        value);

    [Fact]
    public void TheReviewedBudgetFileCoversBothProfilesWithPositiveMeasuredBaselines()
    {
        var budgets = ProfileBudgets.Parse(File.ReadAllBytes(BudgetPath));

        Assert.Equal(10, budgets.RegressionPercent);
        Assert.Equal(["account", "chat"], budgets.Profiles.Keys.OrderBy(key => key, StringComparer.Ordinal));
        foreach (var profile in budgets.Profiles.Values)
            foreach (var metric in ProfileMeasure.Metrics)
                Assert.True(profile[metric] > 0, metric);
    }

    [Fact]
    public void TheBudgetFileRefusesAMissingOrExtraField()
    {
        Assert.Throws<InvalidOperationException>(() => ProfileBudgets.Parse(Encoding.UTF8.GetBytes(
            "{\"schema\": 1, \"regressionPercent\": 10, \"profiles\": {}}")));
        Assert.Throws<InvalidOperationException>(() => ProfileBudgets.Parse(Encoding.UTF8.GetBytes(Document(extra: ", \"unreviewed\": 1"))));
    }

    [Fact]
    public void TheBudgetFileRefusesAProfileOutsideTheBundleOrAMissingMetric()
    {
        var metrics = string.Join(", ", ProfileMeasure.Metrics.Skip(1).Select(metric => $"\"{metric}\": 1000"));
        var missingOne = Document(profiles: $"\"account\": {{{metrics}}}, \"chat\": {{{metrics}}}");
        Assert.Throws<InvalidOperationException>(() => ProfileBudgets.Parse(Encoding.UTF8.GetBytes(missingOne)));

        var extraProfile = Document(profiles: "\"account\": {}, \"chat\": {}, \"operations\": {}");
        Assert.Throws<InvalidOperationException>(() => ProfileBudgets.Parse(Encoding.UTF8.GetBytes(extraProfile)));
    }

    [Fact]
    public void ABaselineMustBeAPositiveMeasuredValue()
    {
        var metrics = string.Join(", ", ProfileMeasure.Metrics.Skip(1).Select(metric => $"\"{metric}\": 1"));
        var zero = Document(profiles: $"\"account\": {{\"initialRequests\": 0, {metrics}}}, \"chat\": {{{metrics}, \"initialRequests\": 1}}");
        Assert.Throws<InvalidOperationException>(() => ProfileBudgets.Parse(Encoding.UTF8.GetBytes(zero)));
    }

    [Fact]
    public void TheRegressionAllowsExactlyTenPercentGrowthAndRoundsDown()
    {
        Assert.Equal(1100, ProfileBudgets.Limit(1000, 10));
        Assert.Equal(1098, ProfileBudgets.Limit(999, 10));
        Assert.Equal(1000, ProfileBudgets.Limit(1000, 0));
    }

    [Fact]
    public void AMeasureAtTheLimitPassesAndOneByteAboveItFailsNamingTheMetric()
    {
        var budgets = ProfileBudgets.Parse(Encoding.UTF8.GetBytes(Document()));

        var report = ProfileBudget.Check(Measure(1000), budgets);
        Assert.Contains("account.totalGzip: 1000 (baseline 1000, limit 1100)", report, StringComparison.Ordinal);

        var at = Measure(1100);
        Assert.Contains("limit 1100", ProfileBudget.Check(at, budgets), StringComparison.Ordinal);

        var above = Measure(1101);
        var error = Assert.Throws<InvalidOperationException>(() => ProfileBudget.Check(above, budgets));
        Assert.Contains("account.initialRequests", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGzipMeasureIsTheLengthOfTheCompressedBytesAndIsStable()
    {
        var bytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("blazor profile ", 500)));

        var first = ProfileBudget.GzipLength(bytes);
        var second = ProfileBudget.GzipLength(bytes);

        Assert.Equal(first, second);
        Assert.True(first > 0 && first < bytes.Length);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(bytes);
        Assert.Equal(output.Length, first);
    }

    [Fact]
    public void TheMeasureCountsTheInitialFilesAndIgnoresPrecompressedSiblings()
    {
        var root = Path.Combine(Path.GetTempPath(), "arcforges-budget-" + Guid.NewGuid().ToString("N"));
        try
        {
            var wwwroot = Path.Combine(root, "wwwroot");
            Directory.CreateDirectory(Path.Combine(wwwroot, "_framework"));
            File.WriteAllText(Path.Combine(wwwroot, "index.html"), "<html></html>");
            File.WriteAllText(Path.Combine(wwwroot, "app.css"), "body{margin:0}");
            File.WriteAllText(Path.Combine(wwwroot, "favicon.svg"), "<svg/>");
            File.WriteAllText(Path.Combine(wwwroot, "_framework", "dotnet.native.b6l13xorvf.js"), "export {};");
            File.WriteAllText(Path.Combine(wwwroot, "_framework", "ArcForges.Web.App.w1s8cjv5ju.wasm"), "wasm");
            File.WriteAllText(Path.Combine(wwwroot, "_framework", "icudt_CJK.tjcz0u77k5.dat"), "data");
            File.WriteAllText(Path.Combine(wwwroot, "_framework", "ArcForges.Web.App.w1s8cjv5ju.wasm.gz"), "precompressed");

            var measure = ProfileBudget.Measure(root);

            Assert.Equal(6, measure.InitialRequests);
            Assert.Equal(Encoding.UTF8.GetBytes("<html></html>").Length, measure.HtmlBytes);
            Assert.Equal(ProfileBudget.GzipLength(Encoding.UTF8.GetBytes("body{margin:0}")), measure.InitialCssGzip);
            Assert.Equal(ProfileBudget.GzipLength(Encoding.UTF8.GetBytes("export {};")), measure.InitialJsGzip);
            Assert.Equal(ProfileBudget.GzipLength(Encoding.UTF8.GetBytes("wasm")), measure.InitialWasmGzip);
            Assert.Equal(ProfileBudget.GzipLength(Encoding.UTF8.GetBytes("data")), measure.InitialDataGzip);
            Assert.Equal(ProfileBudget.GzipLength(Encoding.UTF8.GetBytes("<svg/>")), measure.InitialOtherGzip);
            Assert.Equal(
                measure.InitialCssGzip + measure.InitialJsGzip + measure.InitialWasmGzip + measure.InitialDataGzip + measure.InitialOtherGzip,
                measure.TotalGzip);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AMeasureWithoutAShellOrWwwrootIsRefused()
    {
        var root = Path.Combine(Path.GetTempPath(), "arcforges-budget-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Assert.Throws<InvalidOperationException>(() => ProfileBudget.Measure(root));
            Directory.CreateDirectory(Path.Combine(root, "wwwroot"));
            Assert.Throws<InvalidOperationException>(() => ProfileBudget.Measure(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
