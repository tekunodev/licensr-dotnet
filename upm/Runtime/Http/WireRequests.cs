using System.Text.Json;
using System.Text.Json.Serialization;

namespace Licensr.Sdk.Http;

/// <summary>
/// snake_case request bodies sent to the license API. Kept separate from
/// the public, PascalCase request types (see <c>Types.cs</c>) so those stay
/// free of wire-format concerns — mirrors <c>sdks/js/src/client.ts</c>'s
/// inline body construction.
/// </summary>
internal sealed class ValidateRequestWire
{
    [JsonPropertyName("license_key")]
    public string LicenseKey { get; set; } = "";

    [JsonPropertyName("plugin_slug")]
    public string PluginSlug { get; set; } = "";
}

internal sealed class ActivateRequestWire
{
    [JsonPropertyName("license_key")]
    public string LicenseKey { get; set; } = "";

    [JsonPropertyName("plugin_slug")]
    public string PluginSlug { get; set; } = "";

    [JsonPropertyName("activation_type")]
    public ActivationType ActivationType { get; set; }

    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = "";

    [JsonPropertyName("label")]
    public string? Label { get; set; }
}

internal sealed class DeactivateRequestWire
{
    [JsonPropertyName("license_key")]
    public string LicenseKey { get; set; } = "";

    [JsonPropertyName("activation_id")]
    public string ActivationId { get; set; } = "";
}

internal sealed class CheckoutRequestWire
{
    [JsonPropertyName("plan_id")]
    public string PlanId { get; set; } = "";

    [JsonPropertyName("customer_email")]
    public string CustomerEmail { get; set; } = "";

    [JsonPropertyName("success_url")]
    public string? SuccessUrl { get; set; }

    [JsonPropertyName("cancel_url")]
    public string? CancelUrl { get; set; }

    // Sent only when true, so servers without test clocks see the same body.
    [JsonPropertyName("test_clock")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool TestClock { get; set; }
}

/// <summary>Every non-2xx license/billing response body: <c>{"detail": ...}</c>, where <c>detail</c> is either an object or a validation-error array — see <see cref="ErrorDetailWire"/>/<see cref="ValidationErrorItemWire"/>.</summary>
internal sealed class ErrorEnvelopeWire
{
    [JsonPropertyName("detail")]
    public JsonElement Detail { get; set; }
}

/// <summary>FastAPI's hand-written <c>{"detail": {"error": ..., "message": ...}}</c> shape.</summary>
internal sealed class ErrorDetailWire
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>Pydantic's own 422 validation-error item shape, e.g. <c>{"detail": [{"msg": ..., "loc": [...]}]}</c>.</summary>
internal sealed class ValidationErrorItemWire
{
    [JsonPropertyName("msg")]
    public string? Msg { get; set; }
}
