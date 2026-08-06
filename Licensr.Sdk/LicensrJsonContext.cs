using System.Text.Json.Serialization;
using Licensr.Sdk.Http;
using Licensr.Sdk.Offline;

namespace Licensr.Sdk;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for every type this
/// SDK (de)serializes. Avoids <see cref="System.Text.Json"/>'s
/// reflection-based fallback serializer, which IL2CPP's ahead-of-time
/// compiler either can't handle or handles slowly — see
/// https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation.
/// </summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ValidateResult))]
[JsonSerializable(typeof(ActivateResult))]
[JsonSerializable(typeof(DeactivateResult))]
[JsonSerializable(typeof(ActivationsResult))]
[JsonSerializable(typeof(TokenResult))]
[JsonSerializable(typeof(CheckoutResult))]
[JsonSerializable(typeof(OfflineTokenClaims))]
[JsonSerializable(typeof(ValidateRequestWire))]
[JsonSerializable(typeof(ActivateRequestWire))]
[JsonSerializable(typeof(DeactivateRequestWire))]
[JsonSerializable(typeof(CheckoutRequestWire))]
[JsonSerializable(typeof(ErrorEnvelopeWire))]
[JsonSerializable(typeof(ErrorDetailWire))]
[JsonSerializable(typeof(ValidationErrorItemWire))]
[JsonSerializable(typeof(List<ValidationErrorItemWire>))]
[JsonSerializable(typeof(Jwks))]
[JsonSerializable(typeof(JwsHeaderWire))]
internal partial class LicensrJsonContext : JsonSerializerContext
{
}
