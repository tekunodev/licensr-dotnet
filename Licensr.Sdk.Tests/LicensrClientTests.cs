using Licensr.Sdk.Http;
using Xunit;

namespace Licensr.Sdk.Tests;

public class LicensrClientTests
{
    private static LicensrClient MakeClient(FakeHttpTransport transport, RetryOptions? retry = null, string? deviceId = null, string? origin = null) =>
        new(new LicensrClientConfig
        {
            ApiKey = "pk_test_abc",
            PluginSlug = "my-plugin",
            Transport = transport,
            DeviceId = deviceId,
            Origin = origin,
            Retry = retry ?? new RetryOptions { BaseDelayMs = 1, MaxDelayMs = 2 },
        });

    [Fact]
    public async Task ValidateAsync_ParsesResponse_AndFiresValidatedEvent()
    {
        var transport = new FakeHttpTransport().EnqueueJson(200, """
            {"valid": true, "status": "active", "entitlement": "full", "feature_flags": {"beta": true},
             "license": {"plan_id": "pro", "activation_mode": "seat", "max_seats": 3, "max_domains": null,
                         "seats_in_use": 1, "expires_at": null}}
            """);
        var client = MakeClient(transport);
        ValidateResult? fired = null;
        client.Validated += (_, result) => fired = result;

        var result = await client.ValidateAsync(new ValidateRequest { LicenseKey = "lic_abc" });

        Assert.True(result.Valid);
        Assert.Equal(LicenseStatus.Active, result.Status);
        Assert.Equal("pro", result.License.PlanId);
        Assert.Equal(ActivationType.Seat, result.License.ActivationMode);
        Assert.Equal(3, result.License.MaxSeats);
        Assert.Null(result.License.MaxDomains);
        Assert.Equal(Entitlement.Full, result.Entitlement);
        Assert.True(result.FeatureFlags!.Value.GetProperty("beta").GetBoolean());
        Assert.Same(result, fired);

        var sent = Assert.Single(transport.Requests);
        Assert.Equal("Bearer pk_test_abc", sent.Headers["Authorization"]);
        Assert.Contains("v1/license/validate", sent.Url);
        Assert.Contains("\"license_key\":\"lic_abc\"", sent.Body);
        Assert.Contains("\"plugin_slug\":\"my-plugin\"", sent.Body);
    }

    [Fact]
    public async Task ActivateAsync_ParsesNestedActivation()
    {
        var transport = new FakeHttpTransport().EnqueueJson(200, """
            {"activation_id": "act-1", "feature_flags": null,
             "activation": {"type": "domain", "identifier": "example.com"}}
            """);
        var client = MakeClient(transport);

        var result = await client.ActivateAsync(new ActivateRequest
        {
            LicenseKey = "lic_abc",
            ActivationType = ActivationType.Domain,
            Identifier = "example.com",
        });

        Assert.Equal("act-1", result.ActivationId);
        Assert.Equal(ActivationType.Domain, result.Activation.Type);
        Assert.Equal("example.com", result.Activation.Identifier);
    }

    [Fact]
    public async Task TokenAsync_ParsesNestedDetails()
    {
        var transport = new FakeHttpTransport().EnqueueJson(200, """
            {"token": "jwt-value",
             "details": {"algorithm": "EdDSA", "kid": "kid-1", "issued_at": "2027-01-01T00:00:00Z",
                         "expires_at": "2027-02-01T00:00:00Z", "jwks_url": "https://api.licensr.app/v1/license/jwks/acme/p.json"}}
            """);
        var client = MakeClient(transport);

        var result = await client.TokenAsync(new ValidateRequest { LicenseKey = "lic_abc" });

        Assert.Equal("jwt-value", result.Token);
        Assert.Equal("kid-1", result.Details.Kid);
        Assert.Equal("https://api.licensr.app/v1/license/jwks/acme/p.json", result.Details.JwksUrl);
    }

    [Fact]
    public async Task ActivationsAsync_SendsQueryParams_NotABody()
    {
        var transport = new FakeHttpTransport().EnqueueJson(200, """{"license_id": "lic_1", "activations": []}""");
        var client = MakeClient(transport);

        var result = await client.ActivationsAsync(new ActivationsRequest { LicenseKey = "lic_abc" });

        Assert.Equal("lic_1", result.LicenseId);
        Assert.Empty(result.Activations);
        var sent = Assert.Single(transport.Requests);
        Assert.Null(sent.Body);
        Assert.Contains("license_key=lic_abc", sent.Url);
        Assert.Contains("plugin_slug=my-plugin", sent.Url);
    }

    [Fact]
    public async Task DeviceIdAndOrigin_AreSentAsHeaders_WhenConfigured()
    {
        var transport = new FakeHttpTransport().EnqueueJson(200, """{"deactivated": true}""");
        var client = MakeClient(transport, deviceId: "hwid-123", origin: "app://my-plugin");

        await client.DeactivateAsync(new DeactivateRequest { LicenseKey = "lic_abc", ActivationId = "act_1" });

        var sent = Assert.Single(transport.Requests);
        Assert.Equal("hwid-123", sent.Headers["X-Licensr-Device-Id"]);
        Assert.Equal("app://my-plugin", sent.Headers["Origin"]);
    }

    [Fact]
    public async Task ApiError_WithHandWrittenDetailShape_ThrowsLicensrApiException()
    {
        var transport = new FakeHttpTransport().EnqueueJson(403, """{"detail": {"error": "origin_not_allowed", "message": "nope"}}""");
        var client = MakeClient(transport);
        LicensrErrorEventArgs? fired = null;
        client.Error += (_, args) => fired = args;

        var ex = await Assert.ThrowsAsync<LicensrApiException>(() => client.ValidateAsync(new ValidateRequest { LicenseKey = "lic_abc" }));

        Assert.Equal(403, ex.Status);
        Assert.Equal("origin_not_allowed", ex.Code);
        Assert.Equal("nope", ex.Message);
        Assert.Same(ex, fired?.Exception);
        Assert.Equal("validate", fired?.Method);
    }

    [Fact]
    public async Task ApiError_WithPydanticValidationShape_MapsToValidationError()
    {
        var transport = new FakeHttpTransport().EnqueueJson(422, """{"detail": [{"msg": "field required", "loc": ["body", "license_key"]}]}""");
        var client = MakeClient(transport);

        var ex = await Assert.ThrowsAsync<LicensrApiException>(() => client.ValidateAsync(new ValidateRequest { LicenseKey = "" }));

        Assert.Equal(422, ex.Status);
        Assert.Equal("validation_error", ex.Code);
        Assert.Equal("field required", ex.Message);
    }

    [Fact]
    public async Task RetriesOn5xx_ThenSucceeds_AndFiresRetryEvent()
    {
        var transport = new FakeHttpTransport()
            .EnqueueJson(503, """{"detail": {"error": "unavailable", "message": "down"}}""")
            .EnqueueJson(200, """{"valid": false, "status": "inactive", "entitlement": "none", "feature_flags": null, "license": {"plan_id": null, "activation_mode": null, "max_seats": null, "max_domains": null, "seats_in_use": null, "expires_at": null}}""");
        var client = MakeClient(transport);
        var retries = new List<int>();
        client.Retry += (_, args) => retries.Add(args.Attempt);

        var result = await client.ValidateAsync(new ValidateRequest { LicenseKey = "lic_abc" });

        Assert.False(result.Valid);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal([1], retries);
    }

    [Fact]
    public async Task RetryExhausted_OnRepeated5xx_ThrowsApiException()
    {
        var transport = new FakeHttpTransport()
            .EnqueueJson(500, "{}")
            .EnqueueJson(500, "{}")
            .EnqueueJson(500, "{}");
        var client = MakeClient(transport, retry: new RetryOptions { MaxRetries = 2, BaseDelayMs = 1, MaxDelayMs = 2 });

        var ex = await Assert.ThrowsAsync<LicensrApiException>(() => client.ValidateAsync(new ValidateRequest { LicenseKey = "lic_abc" }));

        Assert.Equal(500, ex.Status);
        Assert.Equal(3, transport.Requests.Count); // initial attempt + 2 retries
    }

    [Fact]
    public async Task NetworkFailure_RetriesThenThrowsNetworkException()
    {
        var transport = new FakeHttpTransport()
            .EnqueueThrows(new Exception("DNS failure"))
            .EnqueueThrows(new Exception("DNS failure"))
            .EnqueueThrows(new Exception("DNS failure"));
        var client = MakeClient(transport, retry: new RetryOptions { MaxRetries = 2, BaseDelayMs = 1, MaxDelayMs = 2 });

        var ex = await Assert.ThrowsAsync<LicensrNetworkException>(() => client.ValidateAsync(new ValidateRequest { LicenseKey = "lic_abc" }));

        Assert.Contains("failed after 3 attempt(s)", ex.Message);
    }

    [Fact]
    public async Task RateLimited_HonorsRetryAfterHeader_ThenSucceeds()
    {
        var transport = new FakeHttpTransport()
            .EnqueueJson(429, """{"detail": {"error": "rate_limit_exceeded", "message": "slow down"}}""", new Dictionary<string, string> { ["Retry-After"] = "0" })
            .EnqueueJson(200, """{"checkout_url": "https://pay.example/session"}""");
        var client = MakeClient(transport);

        var result = await client.CheckoutAsync(new CheckoutRequest { PlanId = "pro", CustomerEmail = "a@b.com" });

        Assert.Equal("https://pay.example/session", result.CheckoutUrl);
        Assert.Null(result.TestClockId);
        Assert.DoesNotContain("test_clock", transport.Requests[^1].Body);
    }

    [Fact]
    public async Task CheckoutAsync_SendsTestClock_AndParsesTestClockId()
    {
        var transport = new FakeHttpTransport()
            .EnqueueJson(200, """{"checkout_url": "https://pay.example/session", "test_clock_id": "clock_123"}""");
        var client = MakeClient(transport);

        var result = await client.CheckoutAsync(new CheckoutRequest { PlanId = "pro", CustomerEmail = "a@b.com", TestClock = true });

        Assert.Equal("clock_123", result.TestClockId);
        var sent = Assert.Single(transport.Requests);
        Assert.Contains("v1/billing/checkout", sent.Url);
        Assert.Contains("\"test_clock\":true", sent.Body);
    }
}
