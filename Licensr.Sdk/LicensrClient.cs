using Licensr.Sdk.Http;

namespace Licensr.Sdk;

/// <summary>
/// Thin client for the Licensr public license API (<c>/v1/license/*</c>,
/// <c>/v1/billing/checkout</c>).
/// </summary>
/// <remarks>
/// <code>
/// var client = new LicensrClient(new LicensrClientConfig {ApiKey = "pk_live_...", PluginSlug = "my-plugin"});
/// var result = await client.ValidateAsync(new ValidateRequest {LicenseKey = userEnteredKey});
/// if (result.Valid) { /* unlock full features */ }
/// </code>
/// Every method is <c>async</c>/<see cref="Task"/>-returning (awaitable from
/// a Unity coroutine via <c>yield return</c> on the task, or from
/// <c>UniTask</c> via <c>.AsUniTask()</c>) and throws
/// <see cref="LicensrApiException"/> or <see cref="LicensrNetworkException"/>
/// on failure — see the <c>Errors</c> section of <c>sdks/dotnet/README.md</c>.
/// Subscribe to lifecycle events via <see cref="Validated"/>, <see cref="Retry"/>, <see cref="Error"/>, etc.
/// </remarks>
public sealed class LicensrClient
{
    private readonly RequestExecutor _http;
    private readonly string _pluginSlug;

    public event EventHandler<ValidateResult>? Validated;
    public event EventHandler<ActivateResult>? Activated;
    public event EventHandler<DeactivateResult>? Deactivated;
    public event EventHandler<TokenResult>? TokenIssued;
    public event EventHandler<RetryEventArgs>? Retry;
    public event EventHandler<LicensrErrorEventArgs>? Error;

    public LicensrClient(LicensrClientConfig config)
    {
        _pluginSlug = config.PluginSlug;
        _http = new RequestExecutor(config);
    }

    /// <summary>Check whether a license is valid for this plugin, without minting an offline token.</summary>
    public Task<ValidateResult> ValidateAsync(ValidateRequest request, CancellationToken cancellationToken = default) =>
        RunAsync("validate", async () =>
        {
            var body = new ValidateRequestWire { LicenseKey = request.LicenseKey, PluginSlug = _pluginSlug };
            var result = await _http
                .PostAsync(
                    "v1/license/validate",
                    body,
                    LicensrJsonContext.Default.ValidateRequestWire,
                    LicensrJsonContext.Default.ValidateResult,
                    OnRetry("validate"),
                    cancellationToken)
                .ConfigureAwait(false);
            Validated?.Invoke(this, result);
            return result;
        });

    /// <summary>
    /// Run the same check as <see cref="ValidateAsync"/>, and on success mint
    /// a short-lived EdDSA-signed offline token. Verify it with
    /// <see cref="Offline.OfflineTokenVerifier.VerifyAsync"/> — no network
    /// call needed after the first successful fetch of the plugin's JWKS.
    /// </summary>
    public Task<TokenResult> TokenAsync(ValidateRequest request, CancellationToken cancellationToken = default) =>
        RunAsync("token", async () =>
        {
            var body = new ValidateRequestWire { LicenseKey = request.LicenseKey, PluginSlug = _pluginSlug };
            var result = await _http
                .PostAsync(
                    "v1/license/token",
                    body,
                    LicensrJsonContext.Default.ValidateRequestWire,
                    LicensrJsonContext.Default.TokenResult,
                    OnRetry("token"),
                    cancellationToken)
                .ConfigureAwait(false);
            TokenIssued?.Invoke(this, result);
            return result;
        });

    /// <summary>Activate a seat (per-machine HWID) or domain for a license.</summary>
    public Task<ActivateResult> ActivateAsync(ActivateRequest request, CancellationToken cancellationToken = default) =>
        RunAsync("activate", async () =>
        {
            var body = new ActivateRequestWire
            {
                LicenseKey = request.LicenseKey,
                PluginSlug = _pluginSlug,
                ActivationType = request.ActivationType,
                Identifier = request.Identifier,
                Label = request.Label,
            };
            var result = await _http
                .PostAsync(
                    "v1/license/activate",
                    body,
                    LicensrJsonContext.Default.ActivateRequestWire,
                    LicensrJsonContext.Default.ActivateResult,
                    OnRetry("activate"),
                    cancellationToken)
                .ConfigureAwait(false);
            Activated?.Invoke(this, result);
            return result;
        });

    /// <summary>Release a previously created activation, freeing its seat/domain slot.</summary>
    public Task<DeactivateResult> DeactivateAsync(DeactivateRequest request, CancellationToken cancellationToken = default) =>
        RunAsync("deactivate", async () =>
        {
            var body = new DeactivateRequestWire { LicenseKey = request.LicenseKey, ActivationId = request.ActivationId };
            var result = await _http
                .PostAsync(
                    "v1/license/deactivate",
                    body,
                    LicensrJsonContext.Default.DeactivateRequestWire,
                    LicensrJsonContext.Default.DeactivateResult,
                    OnRetry("deactivate"),
                    cancellationToken)
                .ConfigureAwait(false);
            Deactivated?.Invoke(this, result);
            return result;
        });

    /// <summary>List every current activation for a license (seats/domains in use).</summary>
    public Task<ActivationsResult> ActivationsAsync(ActivationsRequest request, CancellationToken cancellationToken = default) =>
        RunAsync("activations", () =>
            _http.GetAsync(
                "v1/license/activations",
                new Dictionary<string, string?> { ["license_key"] = request.LicenseKey, ["plugin_slug"] = _pluginSlug },
                LicensrJsonContext.Default.ActivationsResult,
                OnRetry("activations"),
                cancellationToken));

    /// <summary>Start a hosted checkout session for a plan; direct the user to the returned URL.</summary>
    public Task<CheckoutResult> CheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default) =>
        RunAsync("checkout", () =>
        {
            var body = new CheckoutRequestWire
            {
                PlanId = request.PlanId,
                CustomerEmail = request.CustomerEmail,
                SuccessUrl = request.SuccessUrl,
                CancelUrl = request.CancelUrl,
                TestClock = request.TestClock,
            };
            return _http.PostAsync(
                "v1/billing/checkout",
                body,
                LicensrJsonContext.Default.CheckoutRequestWire,
                LicensrJsonContext.Default.CheckoutResult,
                OnRetry("checkout"),
                cancellationToken);
        });

    private Action<int, int> OnRetry(string method) => (attempt, delayMs) =>
        Retry?.Invoke(this, new RetryEventArgs { Method = method, Attempt = attempt, DelayMs = delayMs });

    private async Task<T> RunAsync<T>(string method, Func<Task<T>> fn)
    {
        try
        {
            return await fn().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Error?.Invoke(this, new LicensrErrorEventArgs { Method = method, Exception = exception });
            throw;
        }
    }
}
