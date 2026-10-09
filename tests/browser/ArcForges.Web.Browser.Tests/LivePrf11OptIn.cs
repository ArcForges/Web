// SPDX-License-Identifier: AGPL-3.0-only
// The PRF.11 live opt-in gate (U7). The live specs run only on a developer machine, with an explicit proof origin, and they
// never run on a CI host. They are test-only: nothing here is executed by the offline gate, and nothing is a CI check (P2-017).
// A second switch marks the cloud parts as ready. It is set only after CLOUD.21 and CLOUD.22 are delivered and the deployed
// ingress has been observed, so the cloud specs are skipped as "blocked on CLOUD.21/CLOUD.22, not proven" until then.
namespace ArcForges.Web.Browser.Tests;

/// <summary>The PRF.11 live opt-in gate.</summary>
public static class LivePrf11OptIn
{
    /// <summary>The variable that opts a local run into the live PRF.11 specs. It must be "1".</summary>
    public const string OptInVariable = "ARCFORGES_LIVE_PRF11";

    /// <summary>The absolute https origin of the proof deployment (the Account and Chat shells are served under it).</summary>
    public const string ProofOriginVariable = "ARCFORGES_PROOF_ORIGIN";

    /// <summary>The switch that marks the cloud parts ready. It must be "1" and is set only after CLOUD.21, CLOUD.22 and CLOUD.85.</summary>
    public const string CloudReadyVariable = "ARCFORGES_PRF11_CLOUD_READY";

    /// <summary>The installed Chromium executable (Chrome or Edge), with no browser download.</summary>
    public const string ChromiumPathVariable = LocalOptIn.ChromiumPathVariable;

    /// <summary>The folder where a live run records its measurements (created locally, never committed).</summary>
    public const string ResultsVariable = "ARCFORGES_PRF11_RESULTS";

    /// <summary>True only for a deliberate local live run against an explicit https proof origin: never on CI.</summary>
    public static bool IsEnabled(IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (LocalOptIn.IsCi(environment))
            return false;
        return environment.TryGetValue(OptInVariable, out var optIn) && optIn == "1"
            && ProofOrigin(environment) is not null;
    }

    /// <summary>True only when the live run is enabled and the cloud parts are marked ready.</summary>
    public static bool IsCloudReady(IReadOnlyDictionary<string, string?> environment) =>
        IsEnabled(environment)
        && environment.TryGetValue(CloudReadyVariable, out var ready) && ready == "1";

    /// <summary>The proof origin as an absolute https URI with no path, query or user information, or null when it is not one.</summary>
    public static Uri? ProofOrigin(IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.TryGetValue(ProofOriginVariable, out var text) || string.IsNullOrWhiteSpace(text))
            return null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return null;
        if (uri.AbsolutePath != "/")
            return null;
        return uri;
    }

    /// <summary>The message a cloud spec reports when the cloud parts are not marked ready.</summary>
    public const string BlockedCloudReason =
        "Blocked on CLOUD.21/CLOUD.22 (and CLOUD.85 for the shells), not proven. Set ARCFORGES_PRF11_CLOUD_READY=1 only after those are delivered and the deployed ingress is observed.";

    /// <summary>The message a live spec reports when the opt-in is not set.</summary>
    public const string LiveOptInReason =
        "Local live run only: set ARCFORGES_LIVE_PRF11=1 and ARCFORGES_PROOF_ORIGIN to the https proof origin on a developer machine; never on CI.";
}
