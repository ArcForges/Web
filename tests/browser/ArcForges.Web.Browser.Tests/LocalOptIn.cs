// SPDX-License-Identifier: AGPL-3.0-only
// Local opt-in gate for the browser tests. The tests run only when the developer sets ARCFORGES_LOCAL_BROWSER=1 and the
// environment is not a CI host. Nothing here installs a browser or provisions a service.
namespace ArcForges.Web.Browser.Tests;

/// <summary>The opt-in gate: local runs only, refused on any CI host.</summary>
public static class LocalOptIn
{
    /// <summary>The environment variable that opts a local run in.</summary>
    public const string OptInVariable = "ARCFORGES_LOCAL_BROWSER";

    /// <summary>The environment variable that names the already-served base URL (for example a local preview).</summary>
    public const string BaseUrlVariable = "ARCFORGES_BROWSER_BASE_URL";

    /// <summary>The environment variable that names a local axe-core script, injected only into the page under test.</summary>
    public const string AxeScriptVariable = "ARCFORGES_AXE_CORE_PATH";

    /// <summary>The environment variable that names an installed Chromium executable, for a machine whose Playwright build differs.</summary>
    public const string ChromiumPathVariable = "ARCFORGES_CHROMIUM_PATH";

    /// <summary>True only for a deliberate local run with a base URL: never on CI.</summary>
    public static bool IsEnabled(IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (IsCi(environment))
            return false;
        return environment.TryGetValue(OptInVariable, out var optIn) && optIn == "1"
            && environment.TryGetValue(BaseUrlVariable, out var baseUrl) && !string.IsNullOrWhiteSpace(baseUrl);
    }

    /// <summary>True when any common CI marker is set. A CI host never runs the browser tests.</summary>
    public static bool IsCi(IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        foreach (var marker in new[] { "CI", "GITHUB_ACTIONS", "TF_BUILD", "BUILD_BUILDID", "GITLAB_CI" })
            if (environment.TryGetValue(marker, out var value) && !string.IsNullOrEmpty(value))
                return true;
        return false;
    }

    /// <summary>The current process environment as the gate reads it.</summary>
    public static IReadOnlyDictionary<string, string?> Current()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            values[(string)entry.Key] = (string?)entry.Value;
        return values;
    }
}
