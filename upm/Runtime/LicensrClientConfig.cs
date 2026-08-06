using Licensr.Sdk.Http;

namespace Licensr.Sdk;

public sealed class LicensrClientConfig
{
    /// <summary>Plugin API key (<c>pk_live_...</c> / <c>pk_test_...</c>) — see docs/plugin-integration.md.</summary>
    public required string ApiKey { get; init; }

    /// <summary>The plugin this key belongs to (matches the slug in the admin dashboard).</summary>
    public required string PluginSlug { get; init; }

    public string BaseUrl { get; init; } = "https://api.licensr.app";

    /// <summary>
    /// Explicitly set the <c>Origin</c> header. Rarely needed for native
    /// plugins/desktop/Unity apps — prefer setting the plugin's client type
    /// to "native" in the admin UI and leaving this unset.
    /// </summary>
    public string? Origin { get; init; }

    /// <summary>
    /// Stable per-install identifier — buckets rate limits per
    /// installation instead of per key. In Unity, <c>SystemInfo.deviceUniqueIdentifier</c>
    /// is a reasonable default; reuse whatever you pass as <see cref="ActivateRequest.Identifier"/>.
    /// </summary>
    public string? DeviceId { get; init; }

    /// <summary>0 (default) means no per-request timeout.</summary>
    public int TimeoutMs { get; init; }

    public RetryOptions Retry { get; init; } = new();

    /// <summary>
    /// Bring your own HTTP stack instead of the default
    /// <see cref="HttpClientTransport"/> — e.g. a <c>UnityWebRequest</c>-backed
    /// transport for WebGL, where <see cref="System.Net.Http.HttpClient"/> isn't supported.
    /// </summary>
    public IHttpTransport Transport { get; init; } = new HttpClientTransport();
}
