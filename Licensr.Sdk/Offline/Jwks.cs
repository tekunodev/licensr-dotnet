using System.Text.Json.Serialization;

namespace Licensr.Sdk.Offline;

/// <summary>A single JSON Web Key — only the OKP/Ed25519 shape (RFC 8037) the license API's JWKS endpoint publishes.</summary>
internal sealed class Jwk
{
    [JsonPropertyName("kty")]
    public string Kty { get; set; } = "";

    [JsonPropertyName("crv")]
    public string? Crv { get; set; }

    /// <summary>Base64url-encoded raw 32-byte Ed25519 public key.</summary>
    [JsonPropertyName("x")]
    public string? X { get; set; }

    [JsonPropertyName("kid")]
    public string? Kid { get; set; }

    [JsonPropertyName("alg")]
    public string? Alg { get; set; }
}

internal sealed class Jwks
{
    [JsonPropertyName("keys")]
    public List<Jwk> Keys { get; set; } = new();
}

/// <summary>A compact JWS's decoded header — only the two claims this SDK needs.</summary>
internal sealed class JwsHeaderWire
{
    [JsonPropertyName("alg")]
    public string? Alg { get; set; }

    [JsonPropertyName("kid")]
    public string? Kid { get; set; }
}
