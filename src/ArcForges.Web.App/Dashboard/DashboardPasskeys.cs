// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics.CodeAnalysis;
using ArcForges.Contracts.PublicApi.V1;
using Google.Protobuf;
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
    public async Task<WebAuthnAssertion> ProveAsync(AuthChallenge challenge, Uri origin, CancellationToken cancellationToken)
    {
        var options = challenge.Options;
        if (challenge.Method != AuthMethod.Passkey || challenge.Purpose != AuthPurpose.StepUp ||
            challenge.Challenge.Length is < 16 or > 128 ||
            options is null || !string.Equals(options.RpId, origin.IdnHost, StringComparison.OrdinalIgnoreCase) ||
            options.UserVerification != "required" || options.AllowedCredentials.Count is 0 or > 100 ||
            options.AllowedCredentials.Any(id => id.Length is 0 or > 1024))
        {
            throw new DashboardApiException(DashboardFailure.Incompatible);
        }

        var request = new PasskeyInput(new PublicKeyInput(
            options.RpId,
            challenge.Challenge.ToByteArray(),
            options.AllowedCredentials.Select(id => new CredentialInput("public-key", id.ToByteArray())).ToArray(),
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

        var assertion = new WebAuthnAssertion
        {
            CredentialId = Decode(passkey.RawId, 1024),
            ClientDataJson = Decode(proof.ClientDataJSON, 4096),
            AuthenticatorData = Decode(proof.AuthenticatorData, 4096),
            Signature = Decode(proof.Signature, 1024),
        };
        if (!string.IsNullOrEmpty(proof.UserHandle))
            assertion.UserHandle = Decode(proof.UserHandle, 256);
        return assertion;
    }

    private static ByteString Decode(string? value, int maximumBytes)
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
            return ByteString.CopyFrom(bytes);
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
