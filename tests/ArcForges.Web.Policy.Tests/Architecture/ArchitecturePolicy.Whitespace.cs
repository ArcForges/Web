// SPDX-License-Identifier: AGPL-3.0-only
namespace ArcForges.Web.Policy.Tests.Architecture;

/// <summary>
/// The text-format rules of the audited inputs (WEB.40 U5: the formatting gate that replaces Prettier and Biome). The C#
/// formatter is gated separately by dotnet format in CI; this partial enforces what the formatter does not own across the
/// audited C#, MSBuild, npm, lock and thin TypeScript Worker sources: a final newline and no trailing whitespace. A line
/// ending may be LF or CRLF, because a Windows working copy carries CRLF until Git normalises it (.gitattributes eol=lf).
/// </summary>
public static partial class ArchitecturePolicy
{
    private static void AuditWhitespace(PolicyContext context)
    {
        foreach (var (file, text) in context.Sources)
        {
            if (text.Length == 0)
            {
                context.Report("final-newline", file, "An audited text file must not be empty.");
                continue;
            }

            if (!text.EndsWith('\n') || text.EndsWith("\n\n", StringComparison.Ordinal))
                context.Report("final-newline", file, "An audited text file ends with exactly one line feed.");

            var lines = text.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index].TrimEnd('\r');
                if (line.Length > 0 && char.IsWhiteSpace(line[^1]))
                    context.Report("whitespace", file, "Trailing whitespace on line " + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
            }
        }
    }
}
