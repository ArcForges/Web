// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics.CodeAnalysis;
using ArcForges.Contracts.PublicApi.Http.V1.WebBrowser;
using Microsoft.JSInterop;

namespace ArcForges.Web.App.Dashboard;

/// <summary>Passkey input from the current browser for a Center-issued step-up challenge.</summary>
internal sealed class DashboardPasskeys(IJSRuntime browser)
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(PasskeyInput))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(PublicKeyInput))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(CredentialInput))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicProperties, typeof(BrowserPasskey))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicProperties, typeof(BrowserAssertion))]
    public async Task<WebBrowserWebAuthnAssertion> ProveAsync(WebBrowserAuthChallenge challenge, Uri origin, CancellationToken cancellationToken)
    {
        var options = challenge.Options;
        if (challenge.Method != "passkey" || challenge.Purpose != "stepUp" ||
            options is null || !string.Equals(options.RpId, origin.IdnHost, StringComparison.OrdinalIgnoreCase) ||
            options.UserVerification != "required" || options.AllowedCredentials is { Length: > 64 })
        {
            throw new DashboardApiException(DashboardFailure.Incompatible);
        }

        var challengeBytes = Decode(challenge.Challenge, 32);
        if (challengeBytes.Length != 32)
            throw new DashboardApiException(DashboardFailure.Incompatible);
        var request = new PasskeyInput(new PublicKeyInput(
            options.RpId,
            challengeBytes,
            (options.AllowedCredentials ?? []).Select(id => new CredentialInput("public-key", Decode(id, 1023))).ToArray(),
            options.UserVerification));

        BrowserPasskey? passkey;
        try
        {
            passkey = await browser.InvokeAsync<BrowserPasskey?>("navigator.credentials.get", cancellationToken, request);
        }
        catch (JSException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new DashboardApiException(DashboardFailure.ProofCancelled);
        }
        if (passkey?.Response is not { } proof || passkey.Type != "public-key")
            throw new DashboardApiException(DashboardFailure.ProofCancelled);

        return new WebBrowserWebAuthnAssertion
        {
            CredentialId = Validated(passkey.RawId, 1023),
            ClientDataJson = Validated(proof.ClientDataJSON, 16384),
            AuthenticatorData = Validated(proof.AuthenticatorData, 16384),
            Signature = Validated(proof.Signature, 256),
            UserHandle = string.IsNullOrEmpty(proof.UserHandle) ? null : Validated(proof.UserHandle, 64),
        };
    }

    private static string Validated(string? value, int maximumBytes)
    {
        _ = Decode(value, maximumBytes);
        return value!;
    }

    private static byte[] Decode(string? value, int maximumBytes)
    {
        if (string.IsNullOrEmpty(value) || value.Length > (maximumBytes * 4 + 2) / 3 ||
            value.Any(character => character is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
        {
            throw new DashboardApiException(DashboardFailure.Incompatible);
        }

        try
        {
            var normal = value.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(normal.PadRight((normal.Length + 3) / 4 * 4, '='));
            if (bytes.Length is 0 || bytes.Length > maximumBytes ||
                !string.Equals(Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'), value, StringComparison.Ordinal))
            {
                throw new DashboardApiException(DashboardFailure.Incompatible);
            }
            return bytes;
        }
        catch (FormatException)
        {
            throw new DashboardApiException(DashboardFailure.Incompatible);
        }
    }

    public sealed record PasskeyInput(PublicKeyInput PublicKey);
    public sealed record PublicKeyInput(string RpId, byte[] Challenge, CredentialInput[] AllowCredentials, string UserVerification);
    public sealed record CredentialInput(string Type, byte[] Id);

    public sealed class BrowserPasskey
    {
        public string? RawId { get; set; }
        public string? Type { get; set; }
        public BrowserAssertion? Response { get; set; }
    }

    public sealed class BrowserAssertion
    {
        public string? ClientDataJSON { get; set; }
        public string? AuthenticatorData { get; set; }
        public string? Signature { get; set; }
        public string? UserHandle { get; set; }
    }
}
