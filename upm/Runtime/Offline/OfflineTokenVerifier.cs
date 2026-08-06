using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Licensr.Sdk.Http;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Licensr.Sdk.Offline;

public sealed class VerifyOfflineTokenOptions
{
    /// <summary>Override the transport used for the JWKS lookup — mainly for testing, or a Unity WebGL-friendly transport.</summary>
    public IHttpTransport? Transport { get; init; }

    /// <summary>Reject if the token's <c>exp</c>/<c>nbf</c> are further from now than this many seconds (clock skew tolerance). Default 60.</summary>
    public int ClockToleranceSeconds { get; init; } = 60;
}

/// <summary>
/// Verify an offline license token (from <see cref="LicensrClient.TokenAsync"/>)
/// fully offline: no network call beyond fetching (and caching) the
/// plugin's public JWKS. Throws <see cref="LicensrTokenVerificationException"/>
/// on a bad signature, wrong algorithm, or an expired/not-yet-valid token.
/// </summary>
/// <remarks>
/// Tokens carry no revocation signal — this only proves the token was
/// genuinely issued by Licensr and hasn't expired, not that the license is
/// still active right now. Re-validate online (<see cref="LicensrClient.ValidateAsync"/>/
/// <see cref="LicensrClient.TokenAsync"/>) before the token's <see cref="OfflineTokenClaims.Exp"/>.
/// </remarks>
public static class OfflineTokenVerifier
{
    // A plugin's signing key is essentially never rotated mid-process, so a
    // process-lifetime cache (refreshed once on an unrecognized `kid`, in
    // case it *was* rotated) avoids a JWKS fetch on every verification —
    // mirrors sdks/js/src/offline.ts's `createRemoteJWKSet` cache.
    private static readonly ConcurrentDictionary<string, Jwks> JwksCache = new();

    /// <summary>Drop cached JWKS lookups — call after rotating a plugin's signing key in tests, or to force a fresh fetch.</summary>
    public static void ClearJwksCache() => JwksCache.Clear();

    public static async Task<OfflineTokenClaims> VerifyAsync(
        string token,
        string jwksUrl,
        VerifyOfflineTokenOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new VerifyOfflineTokenOptions();
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                throw new LicensrTokenVerificationException("malformed compact JWS (expected 3 dot-separated parts)");
            }

            var header = JsonSerializer.Deserialize(Base64Url.DecodeToString(parts[0]), LicensrJsonContext.Default.JwsHeaderWire);
            if (header?.Alg != "EdDSA")
            {
                throw new LicensrTokenVerificationException($"unsupported alg '{header?.Alg}' (expected EdDSA)");
            }

            if (string.IsNullOrEmpty(header.Kid))
            {
                throw new LicensrTokenVerificationException("token header is missing 'kid'");
            }

            var jwk = await FindKeyAsync(jwksUrl, header.Kid, options.Transport, cancellationToken).ConfigureAwait(false);
            if (jwk?.X is null)
            {
                throw new LicensrTokenVerificationException($"no key with kid '{header.Kid}' in JWKS at {jwksUrl}");
            }

            VerifySignature(parts, jwk.X);

            var claims = JsonSerializer.Deserialize(Base64Url.Decode(parts[1]), LicensrJsonContext.Default.OfflineTokenClaims)
                ?? throw new LicensrTokenVerificationException("token payload is not a JSON object");

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (claims.Exp + options.ClockToleranceSeconds < now)
            {
                throw new LicensrTokenVerificationException("token has expired");
            }

            if (claims.Nbf - options.ClockToleranceSeconds > now)
            {
                throw new LicensrTokenVerificationException("token is not yet valid ('nbf' is in the future)");
            }

            return claims;
        }
        catch (LicensrTokenVerificationException)
        {
            throw;
        }
        catch (Exception cause)
        {
            throw new LicensrTokenVerificationException($"Offline token verification failed: {cause.Message}");
        }
    }

    private static void VerifySignature(string[] parts, string base64UrlPublicKey)
    {
        var publicKeyBytes = Base64Url.Decode(base64UrlPublicKey);
        if (publicKeyBytes.Length != Ed25519PublicKeyParameters.KeySize)
        {
            throw new LicensrTokenVerificationException($"JWK 'x' is {publicKeyBytes.Length} bytes, expected {Ed25519PublicKeyParameters.KeySize}");
        }

        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = Base64Url.Decode(parts[2]);

        var verifier = new Ed25519Signer();
        verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(publicKeyBytes, 0));
        verifier.BlockUpdate(signingInput, 0, signingInput.Length);
        if (!verifier.VerifySignature(signature))
        {
            throw new LicensrTokenVerificationException("signature verification failed");
        }
    }

    private static async Task<Jwk?> FindKeyAsync(string jwksUrl, string kid, IHttpTransport? transport, CancellationToken cancellationToken)
    {
        if (JwksCache.TryGetValue(jwksUrl, out var cached))
        {
            var match = cached.Keys.FirstOrDefault(k => k.Kid == kid);
            if (match is not null)
            {
                return match;
            }
        }

        // Unknown kid (first lookup, or a rotation since we last cached) —
        // (re)fetch once.
        var fetched = await FetchJwksAsync(jwksUrl, transport ?? new HttpClientTransport(), cancellationToken).ConfigureAwait(false);
        JwksCache[jwksUrl] = fetched;
        return fetched.Keys.FirstOrDefault(k => k.Kid == kid);
    }

    private static async Task<Jwks> FetchJwksAsync(string jwksUrl, IHttpTransport transport, CancellationToken cancellationToken)
    {
        var response = await transport
            .SendAsync(
                new TransportRequest { Method = TransportMethod.Get, Url = jwksUrl, Headers = new Dictionary<string, string>(), TimeoutMs = 0 },
                cancellationToken)
            .ConfigureAwait(false);

        if (response.Status is < 200 or >= 300)
        {
            throw new LicensrTokenVerificationException($"failed to fetch JWKS from {jwksUrl}: HTTP {response.Status}");
        }

        return JsonSerializer.Deserialize(response.Body, LicensrJsonContext.Default.Jwks)
            ?? throw new LicensrTokenVerificationException($"JWKS response from {jwksUrl} was not a JSON object");
    }
}
