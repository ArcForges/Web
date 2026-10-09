// SPDX-License-Identifier: AGPL-3.0-only
// WEB.40 parity gate for the public Site (brief section 10, 2026-10-09 adjudication). The byte gate is replaced by a
// normalised-DOM gate: the recorded React prerender and the C# Site are parsed as HTML and compared structurally, and every
// non-HTML asset stays byte-identical. The normaliser is tested on fixtures in CI. The comparison against the recorded React
// prerender needs that local output, so it runs only when ARCFORGES_SITE_PARITY_REFERENCE names it (a local opt-in run that
// never happens in CI). The result is recorded in docs/web-40-site-parity.md.
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ArcForges.Web.Site.Tests.SiteParity;
using Xunit;

namespace ArcForges.Web.Site.Tests;

public sealed class SiteParityTests
{
    /// <summary>The directory holding the recorded React prerender (for example artifacts/parity/react).</summary>
    internal const string ReferenceVariable = "ARCFORGES_SITE_PARITY_REFERENCE";

    /// <summary>An optional file that receives the parity report of a local run.</summary>
    internal const string ReportVariable = "ARCFORGES_SITE_PARITY_REPORT";

    /// <summary>The source commit that the React prerender was built from (VITE_SOURCE_REF), so the source links match.</summary>
    internal const string ReferenceSourceRef = "80900a1430c0fbb483e4a220e20980e3c18728b3";

    private static readonly string[] HtmlPaths = ["index.html", "hello/index.html", "cloud-hello/index.html", "404.html"];

    private static readonly string[] ByteIdenticalPaths = ["404.css", "favicon.svg", "robots.txt"];

    /// <summary>One SHA-256 source of the React policy, with its leading space (the policy lists them after 'self').</summary>
    private static readonly Regex ScriptHash = new(@" 'sha256-[A-Za-z0-9+/=]+'", RegexOptions.CultureInvariant);

    private static readonly Regex ReferenceStylesheet = new(@"href=""/(assets/[^""]+\.css)""", RegexOptions.CultureInvariant);

    [Fact]
    public void NormalisationAcceptsEquivalentSerialisationsAndRemovesHydrationNodes()
    {
        // The React serialiser and the Razor HtmlRenderer spell the same page differently; none of these may be a difference.
        var react = """
            <!DOCTYPE html><html lang="en"><head><meta charSet="utf-8"/><title>Your hello — ArcForges</title>
            <link rel="modulepreload" href="/assets/entry.client-C6nGypqo.js"/><link rel="stylesheet" href="/assets/root-BPtceTrQ.css"/>
            <script>window.__reactRouterContext={};</script></head><body><!--$--><p>Hello, <!-- -->World<!-- -->!</p>
            <input autoComplete="off" disabled="" value="World"/><button class="button  primary" disabled="" type="submit">Send</button><!--/$--></body></html>
            """;
        var csharp = """
            <!doctype html>
            <html lang="en">
              <head>
                <meta charset="utf-8">
                <title>Your hello &#x2014; ArcForges</title>
                <link rel="stylesheet" href="/assets/site.d4912dbd6dc5e22b.css">
              </head>
              <body>
                <p>Hello, World!</p>
                <input autocomplete="off" disabled value="World">
                <button type="submit" class="button primary" disabled>Send</button>
              </body>
            </html>
            """;

        var reference = HtmlNormaliser.Normalise(react);
        var candidate = HtmlNormaliser.Normalise(csharp);

        Assert.Equal(reference.Lines, candidate.Lines);
        Assert.Empty(HtmlNormaliser.Difference(reference.Lines, candidate.Lines));
        Assert.Equal(1, reference.RemovedScripts);
        Assert.Equal(1, reference.RemovedModulePreloads);
        Assert.True(reference.RemovedComments >= 3);
        Assert.Equal(0, candidate.RemovedScripts + candidate.RemovedModulePreloads + candidate.RemovedComments);
    }

    [Fact]
    public void NormalisationReportsTextAttributeAndStructuralDifferences()
    {
        var reference = HtmlNormaliser.Normalise("<main><p>Hello</p><a href=\"/hello\">Hello example</a></main>");

        var text = HtmlNormaliser.Normalise("<main><p>Hi</p><a href=\"/hello\">Hello example</a></main>");
        var textDifference = HtmlNormaliser.Difference(reference.Lines, text.Lines);
        Assert.Contains("- #text Hello", textDifference);
        Assert.Contains("+ #text Hi", textDifference);

        var attribute = HtmlNormaliser.Normalise("<main><p>Hello</p><a href=\"/cloud-hello\">Hello example</a></main>");
        Assert.Contains("+ <a href=\"/cloud-hello\">", HtmlNormaliser.Difference(reference.Lines, attribute.Lines));

        var structure = HtmlNormaliser.Normalise("<main><p>Hello</p></main>");
        Assert.NotEmpty(HtmlNormaliser.Difference(reference.Lines, structure.Lines));
    }

    [Fact]
    public void AScriptThatEnablesAControlIsRemovedAndTheStaticControlStaysDisabled()
    {
        // The retired live control: the React page disabled the button until a script enabled it. Removing the script leaves
        // the control disabled in the static markup, so the only difference is the removed script (the retirement).
        var react = "<form><button disabled=\"\" type=\"button\">Check</button><script>enable()</script></form>";
        var csharp = "<form><button disabled type=\"button\">Check</button></form>";

        var reference = HtmlNormaliser.Normalise(react);
        var candidate = HtmlNormaliser.Normalise(csharp);

        Assert.Empty(HtmlNormaliser.Difference(reference.Lines, candidate.Lines));
        Assert.Equal(1, reference.RemovedScripts);
    }

    [Fact]
    public void OnlyAHashedStylesheetHrefIsAPlaceholderAndOtherHrefsAreCompared()
    {
        var reference = HtmlNormaliser.Normalise("<link rel=\"stylesheet\" href=\"/assets/root-BPtceTrQ.css\">");
        var candidate = HtmlNormaliser.Normalise("<link rel=\"stylesheet\" href=\"/assets/site.d4912dbd6dc5e22b.css\">");
        Assert.Equal(reference.Lines, candidate.Lines);
        Assert.Contains(reference.Lines, line => line.Contains(HtmlNormaliser.StylesheetPlaceholder, StringComparison.Ordinal));

        // The anchor is nested under <html><head/><body>, so it is not Lines[0]; its own line must keep the href verbatim.
        var anchor = HtmlNormaliser.Normalise("<a href=\"/assets/root-BPtceTrQ.css\">download</a>");
        Assert.Contains("<a href=\"/assets/root-BPtceTrQ.css\">", anchor.Lines);
        Assert.DoesNotContain(anchor.Lines, line => line.Contains(HtmlNormaliser.StylesheetPlaceholder, StringComparison.Ordinal));
    }

    [Fact]
    public void TheOnlyContentSecurityPolicyDifferenceIsTheReactScriptHashes()
    {
        // The React policy listed one SHA-256 per inline script body; the C# Site has no inline script, so script-src is 'self'.
        var reference = "  Content-Security-Policy: default-src 'self'; script-src 'self' 'sha256-aaaa=' 'sha256-bbbb/+='; style-src 'self'";
        var candidate = "  Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'";

        Assert.Equal(candidate, WithoutScriptHashes(reference));
        Assert.Empty(HeaderDifferences([reference], [candidate]));
        Assert.NotEmpty(HeaderDifferences(["  Referrer-Policy: no-referrer"], ["  Referrer-Policy: origin"]));
        Assert.NotEmpty(HeaderDifferences([reference], ["  Content-Security-Policy: default-src 'self'; script-src 'self' 'unsafe-inline'"]));
    }

    [Fact]
    public async Task TheCSharpSiteAgreesWithTheRecordedReactPrerenderWhenItIsNamed()
    {
        var reference = Environment.GetEnvironmentVariable(ReferenceVariable);
        if (string.IsNullOrWhiteSpace(reference) || Environment.GetEnvironmentVariable("CI") is not null)
            Assert.Skip($"Local parity run only: set {ReferenceVariable} to the recorded React prerender directory; never on CI.");
        var root = Path.GetFullPath(reference);
        Assert.True(Directory.Exists(root), $"The React prerender directory does not exist: {root}");

        var site = await SiteBuilder.BuildAsync(new SiteOptions { SourceRef = ReferenceSourceRef }, TestContext.Current.CancellationToken);
        var report = new StringBuilder();
        var failures = new List<string>();

        // File inventory. The reference-only files are the React runtime (hydration bundles and the SPA fallback). The candidate
        // has no file the reference lacks; its content-hashed stylesheet is paired with the reference stylesheet below.
        var referenceFiles = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        var candidateFiles = site.Files.Select(file => file.Path).OrderBy(path => path, StringComparer.Ordinal).ToList();
        var stylesheet = ReferenceStylesheet.Match(File.ReadAllText(Path.Combine(root, "index.html"), Encoding.UTF8)).Groups[1].Value;
        var candidateStylesheet = Assert.Single(candidateFiles, path => path.StartsWith("assets/site.", StringComparison.Ordinal));
        var referenceOnly = referenceFiles.Except(candidateFiles, StringComparer.Ordinal).ToList();
        var candidateOnly = candidateFiles.Except(referenceFiles, StringComparer.Ordinal).ToList();
        var expectedReferenceOnly = referenceFiles
            .Where(path => path == "__spa-fallback.html" || (path.StartsWith("assets/", StringComparison.Ordinal) && path.EndsWith(".js", StringComparison.Ordinal)))
            .ToList();
        report.AppendLine($"Reference files: {referenceFiles.Count}. Candidate files: {candidateFiles.Count}.");
        report.AppendLine($"Reference-only (React runtime, intended): {string.Join(", ", referenceOnly)}");
        report.AppendLine($"Candidate-only: {(candidateOnly.Count == 0 ? "none" : string.Join(", ", candidateOnly))}");
        report.AppendLine($"Stylesheet pair: {stylesheet} and {candidateStylesheet}.");
        if (!referenceOnly.SequenceEqual(expectedReferenceOnly, StringComparer.Ordinal))
            failures.Add("The reference-only files are not exactly the React runtime files.");
        if (candidateOnly.Count > 0)
            failures.Add("The candidate has files the reference does not.");

        // HTML pages: each page is a document, and its normalised structure must be equal.
        foreach (var path in HtmlPaths)
        {
            var candidateFile = site.Find(path) ?? throw new InvalidOperationException($"The Site has no {path}.");
            var referenceHtml = File.ReadAllText(Path.Combine(root, path), Encoding.UTF8);
            var candidateHtml = Encoding.UTF8.GetString(candidateFile.Content);
            if (!referenceHtml.StartsWith("<!DOCTYPE html>", StringComparison.OrdinalIgnoreCase) || !candidateHtml.StartsWith("<!doctype html>", StringComparison.OrdinalIgnoreCase))
                failures.Add($"{path} is not an HTML5 document in both outputs.");
            var normalisedReference = HtmlNormaliser.Normalise(referenceHtml);
            var normalisedCandidate = HtmlNormaliser.Normalise(candidateHtml);
            var difference = HtmlNormaliser.Difference(normalisedReference.Lines, normalisedCandidate.Lines);
            report.AppendLine($"{path}: {normalisedCandidate.Lines.Count} normalised lines; React removed {normalisedReference.RemovedScripts} scripts, {normalisedReference.RemovedModulePreloads} module preloads, {normalisedReference.RemovedComments} comments; differences {difference.Count}.");
            foreach (var line in difference)
                report.AppendLine($"    {line}");
            if (difference.Count > 0)
                failures.Add($"{path} differs after normalisation.");
        }

        // Non-HTML assets: byte-identical, including the stylesheet pair whose name is content-hashed.
        foreach (var path in ByteIdenticalPaths)
        {
            var same = SameBytes(File.ReadAllBytes(Path.Combine(root, path)), site.Find(path)?.Content);
            report.AppendLine($"{path}: {(same ? "byte identical" : "DIFFERENT BYTES")}");
            if (!same)
                failures.Add($"{path} is not byte-identical.");
        }
        var stylesheetSame = SameBytes(File.ReadAllBytes(Path.Combine(root, stylesheet)), site.Find(candidateStylesheet)?.Content);
        report.AppendLine($"{stylesheet} against {candidateStylesheet}: {(stylesheetSame ? "byte identical" : "DIFFERENT BYTES")}");
        if (!stylesheetSame)
            failures.Add("The stylesheet bytes are not identical.");

        // Security headers: every line equal, except the Content-Security-Policy, whose only difference is the script hashes.
        var referenceHeaders = File.ReadAllLines(Path.Combine(root, "_headers"), Encoding.UTF8);
        var candidateHeaders = Encoding.UTF8.GetString(site.Find("_headers")!.Content).TrimEnd('\n').Split('\n');
        var headerDifferences = HeaderDifferences(referenceHeaders, candidateHeaders);
        report.AppendLine($"_headers: {candidateHeaders.Length} lines; differences {headerDifferences.Count}.");
        foreach (var line in headerDifferences)
            report.AppendLine($"    {line}");
        if (headerDifferences.Count > 0)
            failures.Add("The security headers differ beyond the script hashes.");

        var text = report.ToString();
        var reportPath = Environment.GetEnvironmentVariable(ReportVariable);
        if (!string.IsNullOrWhiteSpace(reportPath))
            File.WriteAllText(reportPath, text, new UTF8Encoding(false));
        Assert.True(failures.Count == 0, text + string.Join(Environment.NewLine, failures));
    }

    private static bool SameBytes(byte[] reference, byte[]? candidate) =>
        candidate is not null && SHA256.HashData(reference).AsSpan().SequenceEqual(SHA256.HashData(candidate));

    /// <summary>The header lines that differ, after the React script hashes are removed from its policy.</summary>
    internal static IReadOnlyList<string> HeaderDifferences(IReadOnlyList<string> reference, IReadOnlyList<string> candidate)
    {
        var normalisedReference = reference.Select(WithoutScriptHashes).ToList();
        var normalisedCandidate = candidate.Select(WithoutScriptHashes).ToList();
        return HtmlNormaliser.Difference(normalisedReference, normalisedCandidate);
    }

    /// <summary>A header line with each script hash removed. Only the Content-Security-Policy line can carry one.</summary>
    internal static string WithoutScriptHashes(string line) =>
        line.StartsWith("  Content-Security-Policy:", StringComparison.Ordinal) ? ScriptHash.Replace(line, string.Empty) : line;
}
