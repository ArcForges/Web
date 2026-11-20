// SPDX-License-Identifier: AGPL-3.0-only
using ArcForges.Contracts.PublicApi.Http.V1.WebBrowser;
using Microsoft.JSInterop;

namespace ArcForges.Web.App.Dashboard;

/// <summary>Passkey input from the current browser for a Center-issued step-up challenge.</summary>
internal sealed class DashboardPasskeys(IJSRuntime browser)
{
    private const int CredentialBytes = 1023;
    private const int AssertionPartBytes = 16384;
    private const int SignatureBytes = 256;
    private const int UserHandleBytes = 64;
    private const int ProofTextLimit = 4 + (CredentialBytes * 4 + 2) / 3 + 2 * ((AssertionPartBytes * 4 + 2) / 3) +
        (SignatureBytes * 4 + 2) / 3 + (UserHandleBytes * 4 + 2) / 3;

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
        var allowed = options.AllowedCredentials ?? [];
        foreach (var id in allowed)
            _ = Decode(id, CredentialBytes);

        string? passkey;
        try
        {
            await using var module = await browser.InvokeAsync<IJSObjectReference>("import", cancellationToken, DashboardActivity.ModulePath);
            // Dots cannot occur in base64url strings. The browser bridge needs only these primitive values.
            passkey = await module.InvokeAsync<string?>("getPasskey", cancellationToken,
                options.RpId, challenge.Challenge, string.Join('.', allowed));
        }
        catch (JSException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new DashboardApiException(DashboardFailure.ProofCancelled);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (passkey is null)
            throw new DashboardApiException(DashboardFailure.ProofCancelled);
        if (passkey.Length > ProofTextLimit)
            throw new DashboardApiException(DashboardFailure.Incompatible);
        var proof = passkey.Split('.', 6);
        if (proof.Length != 5)
            throw new DashboardApiException(DashboardFailure.Incompatible);

        return new WebBrowserWebAuthnAssertion
        {
            CredentialId = Validated(proof[0], CredentialBytes),
            ClientDataJson = Validated(proof[1], AssertionPartBytes),
            AuthenticatorData = Validated(proof[2], AssertionPartBytes),
            Signature = Validated(proof[3], SignatureBytes),
            UserHandle = proof[4].Length == 0 ? null : Validated(proof[4], UserHandleBytes),
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

}
