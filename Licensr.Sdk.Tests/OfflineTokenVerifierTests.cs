using System.Text;
using System.Text.Json;
using Licensr.Sdk.Http;
using Licensr.Sdk.Offline;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Xunit;

namespace Licensr.Sdk.Tests;

public class OfflineTokenVerifierTests
{
    private const string JwksUrl = "https://api.licensr.app/v1/license/jwks/my-plugin/test.json";

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string Base64UrlEncode(string json) => Base64UrlEncode(Encoding.UTF8.GetBytes(json));

    private (string Token, string JwksJson) IssueToken(
        string kid = "kid-1",
        string alg = "EdDSA",
        long? exp = null,
        long? nbf = null,
        Ed25519PrivateKeyParameters? signingKey = null)
    {
        var random = new SecureRandom();
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(random));
        var keyPair = generator.GenerateKeyPair();
        var privateKey = signingKey ?? (Ed25519PrivateKeyParameters)keyPair.Private;
        var publicKey = (Ed25519PublicKeyParameters)keyPair.Public;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Triple-`$`/triple-brace raw string interpolation: the payload's
        // literal JSON braces (e.g. the trailing `}}` closing
        // "feature_flags" and the object itself) only need two, so three
        // unambiguously marks interpolation holes.
        var header = $$$"""{"alg":"{{{alg}}}","kid":"{{{kid}}}"}""";
        var payload = $$$"""
            {"sub":"lic_abc","iss":"https://api.licensr.app","iat":{{{now}}},"nbf":{{{nbf ?? now}}},
             "exp":{{{exp ?? now + 3600}}},"plugin_slug":"my-plugin","plan_id":"pro","status":"active",
             "valid":true,"entitlement":"full","fallback":false,"activation_mode":"seat",
             "max_seats":3,"max_domains":null,"license_expires_at":null,"feature_flags":{"beta":true}}
            """;

        var signingInput = $"{Base64UrlEncode(header)}.{Base64UrlEncode(payload)}";
        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, privateKey);
        var messageBytes = Encoding.ASCII.GetBytes(signingInput);
        signer.BlockUpdate(messageBytes, 0, messageBytes.Length);
        var signature = signer.GenerateSignature();

        var token = $"{signingInput}.{Base64UrlEncode(signature)}";
        var jwksJson = $$"""{"keys":[{"kty":"OKP","crv":"Ed25519","kid":"{{kid}}","x":"{{Base64UrlEncode(publicKey.GetEncoded())}}"}]}""";
        return (token, jwksJson);
    }

    [Fact]
    public async Task VerifyAsync_ValidToken_ReturnsClaims()
    {
        OfflineTokenVerifier.ClearJwksCache();
        var (token, jwksJson) = IssueToken();
        var transport = new FakeHttpTransport().EnqueueJson(200, jwksJson);

        var claims = await OfflineTokenVerifier.VerifyAsync(token, JwksUrl, new VerifyOfflineTokenOptions { Transport = transport });

        Assert.Equal("lic_abc", claims.Sub);
        Assert.Equal("my-plugin", claims.PluginSlug);
        Assert.True(claims.Valid);
        Assert.Equal(Entitlement.Full, claims.Entitlement);
        Assert.Equal(ActivationType.Seat, claims.ActivationMode);
        Assert.True(claims.FeatureFlags!.Value.GetProperty("beta").GetBoolean());
    }

    [Fact]
    public async Task VerifyAsync_CachesJwks_AcrossCallsForTheSameUrl()
    {
        OfflineTokenVerifier.ClearJwksCache();
        var (token, jwksJson) = IssueToken();
        var transport = new FakeHttpTransport().EnqueueJson(200, jwksJson);

        await OfflineTokenVerifier.VerifyAsync(token, JwksUrl, new VerifyOfflineTokenOptions { Transport = transport });
        await OfflineTokenVerifier.VerifyAsync(token, JwksUrl, new VerifyOfflineTokenOptions { Transport = transport });

        Assert.Single(transport.Requests); // second call hit the cache, not the transport again.
    }

    [Fact]
    public async Task VerifyAsync_TamperedSignature_Throws()
    {
        OfflineTokenVerifier.ClearJwksCache();
        var (token, jwksJson) = IssueToken();
        // Flip a bit in the decoded signature bytes rather than a
        // base64url character directly — the last character's low bits
        // are sometimes padding-only, so a same-length character swap
        // there wouldn't reliably change the decoded bytes.
        var lastDot = token.LastIndexOf('.');
        var signatureBytes = Convert.FromBase64String(token[(lastDot + 1)..].Replace('-', '+').Replace('_', '/') + "==");
        signatureBytes[0] ^= 0xFF;
        var tampered = token[..lastDot] + "." + Base64UrlEncode(signatureBytes);
        var transport = new FakeHttpTransport().EnqueueJson(200, jwksJson);

        var ex = await Assert.ThrowsAsync<LicensrTokenVerificationException>(
            () => OfflineTokenVerifier.VerifyAsync(tampered, JwksUrl, new VerifyOfflineTokenOptions { Transport = transport }));
        Assert.Contains("signature verification failed", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_ExpiredToken_Throws()
    {
        OfflineTokenVerifier.ClearJwksCache();
        var (token, jwksJson) = IssueToken(exp: DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600);
        var transport = new FakeHttpTransport().EnqueueJson(200, jwksJson);

        var ex = await Assert.ThrowsAsync<LicensrTokenVerificationException>(
            () => OfflineTokenVerifier.VerifyAsync(token, JwksUrl, new VerifyOfflineTokenOptions { Transport = transport, ClockToleranceSeconds = 5 }));
        Assert.Contains("expired", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_NotYetValidToken_Throws()
    {
        OfflineTokenVerifier.ClearJwksCache();
        var (token, jwksJson) = IssueToken(nbf: DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600);
        var transport = new FakeHttpTransport().EnqueueJson(200, jwksJson);

        var ex = await Assert.ThrowsAsync<LicensrTokenVerificationException>(
            () => OfflineTokenVerifier.VerifyAsync(token, JwksUrl, new VerifyOfflineTokenOptions { Transport = transport, ClockToleranceSeconds = 5 }));
        Assert.Contains("not yet valid", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_UnknownKid_RefetchesJwksOnce_ThenThrows()
    {
        OfflineTokenVerifier.ClearJwksCache();
        var (token, _) = IssueToken(kid: "kid-missing");
        var transport = new FakeHttpTransport()
            .EnqueueJson(200, """{"keys":[{"kty":"OKP","crv":"Ed25519","kid":"kid-other","x":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}]}""");

        var ex = await Assert.ThrowsAsync<LicensrTokenVerificationException>(
            () => OfflineTokenVerifier.VerifyAsync(token, JwksUrl, new VerifyOfflineTokenOptions { Transport = transport }));
        Assert.Contains("no key with kid", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_WrongAlgorithm_Throws()
    {
        OfflineTokenVerifier.ClearJwksCache();
        var header = Base64UrlEncode("""{"alg":"RS256","kid":"kid-1"}""");
        var payload = Base64UrlEncode("{}");
        var token = $"{header}.{payload}.sig";

        var ex = await Assert.ThrowsAsync<LicensrTokenVerificationException>(
            () => OfflineTokenVerifier.VerifyAsync(token, JwksUrl, new VerifyOfflineTokenOptions { Transport = new FakeHttpTransport() }));
        Assert.Contains("unsupported alg", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_MalformedToken_Throws()
    {
        var ex = await Assert.ThrowsAsync<LicensrTokenVerificationException>(
            () => OfflineTokenVerifier.VerifyAsync("not-a-jwt", JwksUrl, new VerifyOfflineTokenOptions { Transport = new FakeHttpTransport() }));
        Assert.Contains("malformed compact JWS", ex.Message);
    }
}
