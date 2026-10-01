using System.Text.Json;
using System.Text.Json.Serialization;

namespace Licensr.Sdk;

/// <summary>
/// Request/response shapes for the Licensr public license API.
/// </summary>
/// <remarks>
/// Mirrors <c>contract/openapi.json</c> (generated from the FastAPI backend
/// — see <c>backend/services/models/license.py</c>) field-for-field with
/// <c>sdks/js/src/types.ts</c> and <c>sdks/cpp/include/licensr/licensr.h</c>,
/// adapted to .NET naming (PascalCase properties, snake_case
/// <see cref="JsonPropertyNameAttribute"/> wire names). Keep in sync
/// manually: there is no codegen step, so any field added to the backend's
/// response models should be mirrored here.
/// </remarks>
[JsonConverter(typeof(ActivationTypeJsonConverter))]
public enum ActivationType
{
    Seat,
    Domain,
}

[JsonConverter(typeof(LicenseStatusJsonConverter))]
public enum LicenseStatus
{
    Active,
    Inactive,
    Expired,
}

/// <summary><c>Full</c> when valid, <c>None</c> when cut off, <c>Limited</c> when an expired license on a fallback plan keeps reduced access.</summary>
[JsonConverter(typeof(EntitlementJsonConverter))]
public enum Entitlement
{
    Full,
    Limited,
    None,
}

public sealed class ValidateRequest
{
    public required string LicenseKey { get; init; }
}

/// <summary>Information about the license. Nothing here decides access; safe to ignore.</summary>
public sealed class LicenseDetails
{
    [JsonPropertyName("plan_id")]
    public string? PlanId { get; set; }

    [JsonPropertyName("activation_mode")]
    public ActivationType? ActivationMode { get; set; }

    /// <summary>Devices or users allowed. <c>null</c> means unlimited.</summary>
    [JsonPropertyName("max_seats")]
    public long? MaxSeats { get; set; }

    /// <summary>Websites allowed. <c>null</c> means unlimited.</summary>
    [JsonPropertyName("max_domains")]
    public long? MaxDomains { get; set; }

    [JsonPropertyName("seats_in_use")]
    public long? SeatsInUse { get; set; }

    /// <summary>When the license expires. <c>null</c> means it never expires.</summary>
    [JsonPropertyName("expires_at")]
    public string? ExpiresAt { get; set; }
}

public sealed class ValidateResult
{
    /// <summary>The answer you gate on: <c>true</c> unlocks your product, <c>false</c> keeps it locked.</summary>
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    /// <summary>Why: pick the message to show the customer. Do not gate access on it.</summary>
    [JsonPropertyName("status")]
    public LicenseStatus Status { get; set; }

    /// <summary>Optional. <c>Limited</c> means expired but a fallback plan keeps reduced access.</summary>
    [JsonPropertyName("entitlement")]
    public Entitlement Entitlement { get; set; }

    /// <summary>Optional. Custom per-plan settings you defined.</summary>
    [JsonPropertyName("feature_flags")]
    public JsonElement? FeatureFlags { get; set; }

    /// <summary>Plan, limits and expiry. Informational only.</summary>
    [JsonPropertyName("license")]
    public LicenseDetails License { get; set; } = new();
}

public sealed class ActivateRequest
{
    public required string LicenseKey { get; init; }

    public required ActivationType ActivationType { get; init; }

    /// <summary>Stable per-install identifier: HWID for <see cref="ActivationType.Seat"/>, hostname for <see cref="ActivationType.Domain"/>.</summary>
    public required string Identifier { get; init; }

    /// <summary>Cosmetic, shown in admin/customer portals (e.g. "My MacBook Pro").</summary>
    public string? Label { get; init; }
}

/// <summary>What was activated (an echo of your request). Informational only.</summary>
public sealed class ActivationInfo
{
    [JsonPropertyName("type")]
    public ActivationType Type { get; set; }

    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = "";
}

public sealed class ActivateResult
{
    /// <summary>Save this: you need it to deactivate later.</summary>
    [JsonPropertyName("activation_id")]
    public string ActivationId { get; set; } = "";

    [JsonPropertyName("feature_flags")]
    public JsonElement? FeatureFlags { get; set; }

    [JsonPropertyName("activation")]
    public ActivationInfo Activation { get; set; } = new();
}

public sealed class DeactivateRequest
{
    public required string LicenseKey { get; init; }

    public required string ActivationId { get; init; }
}

public sealed class DeactivateResult
{
    [JsonPropertyName("deactivated")]
    public bool Deactivated { get; set; }
}

public sealed class ActivationListItem
{
    [JsonPropertyName("activation_id")]
    public string ActivationId { get; set; } = "";

    [JsonPropertyName("activation_type")]
    public ActivationType ActivationType { get; set; }

    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = "";

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("first_seen_at")]
    public string FirstSeenAt { get; set; } = "";

    [JsonPropertyName("last_seen_at")]
    public string LastSeenAt { get; set; } = "";
}

public sealed class ActivationsRequest
{
    public required string LicenseKey { get; init; }
}

public sealed class ActivationsResult
{
    [JsonPropertyName("license_id")]
    public string LicenseId { get; set; } = "";

    [JsonPropertyName("activations")]
    public List<ActivationListItem> Activations { get; set; } = new();
}

/// <summary>How to verify the offline token and when to refresh it.</summary>
public sealed class TokenDetails
{
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = "";

    [JsonPropertyName("kid")]
    public string Kid { get; set; } = "";

    [JsonPropertyName("issued_at")]
    public string IssuedAt { get; set; } = "";

    [JsonPropertyName("expires_at")]
    public string ExpiresAt { get; set; } = "";

    [JsonPropertyName("jwks_url")]
    public string JwksUrl { get; set; } = "";
}

public sealed class TokenResult
{
    /// <summary>EdDSA-signed JWT — verify with <see cref="Offline.OfflineTokenVerifier.VerifyAsync"/> (pass <c>Details.JwksUrl</c>).</summary>
    [JsonPropertyName("token")]
    public string Token { get; set; } = "";

    [JsonPropertyName("details")]
    public TokenDetails Details { get; set; } = new();
}

/// <summary>Claims embedded in the offline token (<see cref="TokenResult.Token"/>). Flat, as in any JWT; carries the same facts as <see cref="ValidateResult"/>.</summary>
public sealed class OfflineTokenClaims
{
    [JsonPropertyName("sub")]
    public string Sub { get; set; } = "";

    [JsonPropertyName("iss")]
    public string Iss { get; set; } = "";

    [JsonPropertyName("iat")]
    public long Iat { get; set; }

    [JsonPropertyName("nbf")]
    public long Nbf { get; set; }

    [JsonPropertyName("exp")]
    public long Exp { get; set; }

    [JsonPropertyName("plugin_slug")]
    public string PluginSlug { get; set; } = "";

    [JsonPropertyName("plan_id")]
    public string? PlanId { get; set; }

    [JsonPropertyName("status")]
    public LicenseStatus Status { get; set; }

    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("entitlement")]
    public Entitlement Entitlement { get; set; }

    [JsonPropertyName("fallback")]
    public bool Fallback { get; set; }

    [JsonPropertyName("activation_mode")]
    public ActivationType? ActivationMode { get; set; }

    [JsonPropertyName("max_seats")]
    public long? MaxSeats { get; set; }

    [JsonPropertyName("max_domains")]
    public long? MaxDomains { get; set; }

    [JsonPropertyName("license_expires_at")]
    public long? LicenseExpiresAt { get; set; }

    [JsonPropertyName("feature_flags")]
    public JsonElement? FeatureFlags { get; set; }
}

public sealed class CheckoutRequest
{
    public required string PlanId { get; init; }

    public required string CustomerEmail { get; init; }

    public string? SuccessUrl { get; init; }

    public string? CancelUrl { get; init; }
}

public sealed class CheckoutResult
{
    [JsonPropertyName("checkout_url")]
    public string CheckoutUrl { get; set; } = "";
}

internal sealed class ActivationTypeJsonConverter : JsonConverter<ActivationType>
{
    public override ActivationType Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "seat" => ActivationType.Seat,
            "domain" => ActivationType.Domain,
            var other => throw new JsonException($"Unknown activation_type '{other}'"),
        };

    public override void Write(Utf8JsonWriter writer, ActivationType value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value == ActivationType.Seat ? "seat" : "domain");
}

internal sealed class LicenseStatusJsonConverter : JsonConverter<LicenseStatus>
{
    public override LicenseStatus Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "active" => LicenseStatus.Active,
            "inactive" => LicenseStatus.Inactive,
            "expired" => LicenseStatus.Expired,
            var other => throw new JsonException($"Unknown status '{other}'"),
        };

    public override void Write(Utf8JsonWriter writer, LicenseStatus value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            LicenseStatus.Active => "active",
            LicenseStatus.Inactive => "inactive",
            _ => "expired",
        });
}

internal sealed class EntitlementJsonConverter : JsonConverter<Entitlement>
{
    public override Entitlement Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "full" => Entitlement.Full,
            "limited" => Entitlement.Limited,
            "none" => Entitlement.None,
            var other => throw new JsonException($"Unknown entitlement '{other}'"),
        };

    public override void Write(Utf8JsonWriter writer, Entitlement value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            Entitlement.Full => "full",
            Entitlement.Limited => "limited",
            _ => "none",
        });
}
