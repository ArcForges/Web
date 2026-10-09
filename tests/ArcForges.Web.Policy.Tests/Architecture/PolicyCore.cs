// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ArcForges.Web.Policy.Tests.Architecture;

/// <summary>One refusal from the Web policy suite (the C# successor of the GOV.11 Node/TypeScript mechanism).</summary>
/// <param name="Rule">The rule family, for example <c>wire-source</c> or <c>production-command</c>.</param>
/// <param name="File">The repository-relative POSIX path of the offending input.</param>
/// <param name="Detail">A diagnostic for the reviewer. It never contains a secret.</param>
public sealed record PolicyFinding(string Rule, string File, string Detail);

/// <summary>
/// The text inputs of the policy suite, keyed by repository-relative POSIX path in ordinal order. The repository
/// inventory is the tracked and nonignored file set, exactly as the GOV.11 mechanism read it.
/// </summary>
public static class PolicySources
{
    private static readonly string[] AuditedExtensions =
    [
        ".cs", ".razor", ".csproj", ".fsproj", ".vbproj", ".vcxproj", ".esproj", ".props", ".targets", ".slnx", ".sln",
        ".ts", ".tsx", ".js", ".mjs", ".cjs", ".cts", ".mts", ".gradle", ".kts", ".lockfile",
    ];

    private static readonly string[] AuditedNames =
    [
        "package.json", "package-lock.json", "packages.lock.json", "global.json", "nuget.config", "packages.config",
        ".node-version", ".npmrc", "licence-boundary.json", "yarn.lock", "pnpm-lock.yaml", "bun.lock", "bun.lockb",
        "npm-shrinkwrap.json", "cmakelists.txt", "wrangler.json",
    ];

    /// <summary>Returns true when the policy suite reads the file.</summary>
    public static bool IsAudited(string name)
    {
        var lower = PolicyText.BaseName(name).ToLowerInvariant();
        return AuditedNames.Contains(lower, StringComparer.Ordinal)
            || AuditedExtensions.Any(extension => lower.EndsWith(extension, StringComparison.Ordinal));
    }

    /// <summary>Reads every audited tracked or untracked-but-not-ignored file of the repository.</summary>
    public static IReadOnlyDictionary<string, string> FromRepository(string root)
    {
        var listed = Git(root, "ls-files", "-z", "--cached", "--others", "--exclude-standard");
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in listed.Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal))
        {
            if (!IsAudited(name))
                continue;
            var path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
            files[name] = File.ReadAllText(path, Encoding.UTF8);
        }
        return files;
    }

    /// <summary>Runs git with fixed arguments and returns its standard output.</summary>
    public static string Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git could not be started.");
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        var error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("git " + string.Join(' ', arguments) + " failed: " + error);
        return output;
    }
}

/// <summary>The mutable finding sink shared by the rule families of one audit.</summary>
internal sealed class PolicyContext
{
    private readonly List<PolicyFinding> _findings = [];

    public PolicyContext(IReadOnlyDictionary<string, string> sources)
    {
        Sources = sources;
    }

    public IReadOnlyDictionary<string, string> Sources { get; }

    public IReadOnlyList<PolicyFinding> Findings => _findings;

    public void Report(string rule, string file, string detail) =>
        _findings.Add(new PolicyFinding(rule, file, detail));

    public IReadOnlyList<string> Find(Func<string, bool> predicate) =>
        Sources.Keys.Where(predicate).Order(StringComparer.Ordinal).ToArray();

    public string? Text(string file) => Sources.TryGetValue(file, out var text) ? text : null;
}

/// <summary>Path, comment and parse helpers shared by the rule families.</summary>
internal static class PolicyText
{
    private static readonly Regex XmlComment = new("<!--[\\s\\S]*?-->", RegexOptions.CultureInvariant);

    public static string BaseName(string file) => file[(file.LastIndexOf('/') + 1)..];

    public static string DirectoryOf(string file) =>
        file.IndexOf('/', StringComparison.Ordinal) >= 0 ? file[..file.LastIndexOf('/')] : string.Empty;

    public static string ManifestPath(string directory) =>
        directory is "" or "." ? "package.json" : directory + "/package.json";

    /// <summary>Resolves a relative path against a directory, keeping leading parent segments.</summary>
    public static string Join(string directory, string relative) =>
        Normalize(directory.Length == 0 ? relative : directory + "/" + relative);

    public static string Normalize(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Replace('\\', '/').Split('/'))
        {
            if (part is "" or ".")
                continue;
            if (part == ".." && parts.Count > 0 && parts[^1] != "..")
                parts.RemoveAt(parts.Count - 1);
            else
                parts.Add(part);
        }
        return string.Join('/', parts);
    }

    /// <summary>Removes XML comments, replacing each with a space so that comments cannot join two fragments.</summary>
    public static string StripXmlComments(string text) => XmlComment.Replace(text, " ");

    /// <summary>
    /// Removes C-style comments while keeping string and template contents, so that code inside a comment is never
    /// read as a dependency and a quoted fake import is still visible as text.
    /// </summary>
    public static string StripCode(string text, bool templates)
    {
        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';
            if (current == '/' && next == '/')
            {
                while (index < text.Length && text[index] != '\n')
                    index++;
                if (index < text.Length)
                    builder.Append('\n');
                continue;
            }
            if (current == '/' && next == '*')
            {
                var end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                var stop = end < 0 ? text.Length : end + 2;
                builder.Append(' ');
                index = stop - 1;
                continue;
            }
            if (current == '"' || current == '\'' || (templates && current == '`'))
            {
                var verbatim = current == '"' && index > 0 && text[index - 1] == '@';
                var multiline = verbatim || (templates && current == '`');
                builder.Append(current);
                index++;
                while (index < text.Length)
                {
                    var character = text[index];
                    if (character == '\n' && !multiline)
                    {
                        builder.Append('\n');
                        break;
                    }
                    if (character == '\\' && !verbatim)
                    {
                        builder.Append(character);
                        if (index + 1 < text.Length)
                            builder.Append(text[index + 1]);
                        index += 2;
                        continue;
                    }
                    if (character == current)
                    {
                        if (verbatim && index + 1 < text.Length && text[index + 1] == current)
                        {
                            builder.Append(character).Append(character);
                            index += 2;
                            continue;
                        }
                        builder.Append(character);
                        break;
                    }
                    builder.Append(character);
                    index++;
                }
                continue;
            }
            builder.Append(current);
        }
        return builder.ToString();
    }

    public static JsonObject? ParseObject(string? text)
    {
        if (text is null)
            return null;
        try
        {
            return JsonNode.Parse(text)?.AsObject();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IEnumerable<string> Values(string xml, string element) =>
        Regex.Matches(xml, "<" + element + "(?:\\s[^>]*)?>([^<]*)</" + element + ">", RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value);

    public static bool IsDocument(string? text, out XDocument? document)
    {
        document = null;
        if (text is null)
            return false;
        try
        {
            document = XDocument.Parse(text);
            return true;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    /// <summary>Returns the value of an XML attribute from an element's opening tag text, or null.</summary>
    public static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, "\\s" + name + "=\"([^\"]*)\"", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : null;
    }
}
