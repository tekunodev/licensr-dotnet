# Licensr.Sdk (C# SDK)

Official C# SDK for the [Licensr](https://licensr.app) license API: validation, seat/domain activation, offline EdDSA token verification, and hosted checkout. Targets `netstandard2.1` (Unity, older .NET) and `net8.0`; async-first with `System.Text.Json` source generation throughout so it stays IL2CPP-safe (no reflection-heavy serialization path). Its only runtime dependency is [BouncyCastle.Cryptography](https://github.com/bcgit/bc-csharp) — pure managed code (no P/Invoke, so also IL2CPP-safe) — for offline EdDSA verification, mirroring `sdks/js`'s own single dependency on `jose` for the same job.

See the [plugin integration guide](https://docs.licensr.app/plugin-integration) for the underlying API's concepts (activation modes, entitlement/fallback, rate limits, the embedded-key doctrine) — this README covers the C#-specific surface.

## Install

**NuGet** (.NET desktop/server, Godot's Mono backend, NinjaTrader/cTrader plugins):

```bash
dotnet add package Licensr.Sdk
```

**Unity**: NuGet packages don't resolve well inside Unity's package system, so this SDK ships a separate [UPM](https://docs.unity3d.com/Manual/upm-ui.html) package from [`sdks/dotnet/upm/`](upm/) — add it via the Package Manager's "Install package from git URL":

```
https://github.com/tekunodev/licensr-dotnet.git?path=upm
```

## Quickstart

```csharp
using Licensr.Sdk;

var client = new LicensrClient(new LicensrClientConfig
{
    ApiKey = "pk_live_...", // safe to embed in a distributed build — see §6.1 of plugin-integration.md
    PluginSlug = "my-plugin",
});

var result = await client.ValidateAsync(new ValidateRequest { LicenseKey = userEnteredKey });
if (result.Valid)
{
    UnlockFullFeatures();
}
else if (result.Entitlement == Entitlement.Limited)
{
    UnlockDegradedMode(); // perpetual-fallback: expired, but the plan grants a limited tier
}
```

Every method is `Task`-returning — `await` it directly, `yield return` it from a Unity coroutine, or call `.AsUniTask()` if you use [UniTask](https://github.com/Cysharp/UniTask).

## API

Every method throws on failure — see [Errors](#errors).

| Method                                                            | Wraps                          | Notes                                                                                                            |
| ------------------------------------------------------------------ | ------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `client.ValidateAsync(new ValidateRequest {LicenseKey})`            | `POST /v1/license/validate`     | Always read `Valid`/`Entitlement`; never branch on HTTP status — an unknown key returns `200 {valid: false}`, not `404`. |
| `client.TokenAsync(new ValidateRequest {LicenseKey})`               | `POST /v1/license/token`        | Same check as `ValidateAsync`, plus a short-lived signed offline token. See [Offline verification](#offline-verification). |
| `client.ActivateAsync(new ActivateRequest {...})`                   | `POST /v1/license/activate`     | `ActivationType` is `Seat` (per-machine) or `Domain`, fixed by the plugin's configured mode.                     |
| `client.DeactivateAsync(new DeactivateRequest {...})`               | `POST /v1/license/deactivate`   | Frees a seat/domain slot.                                                                                          |
| `client.ActivationsAsync(new ActivationsRequest {LicenseKey})`      | `GET /v1/license/activations`   | Lists every current activation.                                                                                    |
| `client.CheckoutAsync(new CheckoutRequest {...})`                   | `POST /v1/billing/checkout`     | Returns `CheckoutUrl` — direct the user there. Requires a key with the `checkout` scope.                          |

### Client options

```csharp
new LicensrClientConfig
{
    ApiKey = "pk_live_...",
    PluginSlug = "my-plugin",
    BaseUrl = "https://api.licensr.app", // default; override for self-hosted/staging
    DeviceId = stableHwid, // buckets rate limits per installation instead of per key — see Hardware/device IDs below
    Origin = "app://my-plugin", // rarely needed — prefer setting the plugin's client_kind to "native" instead
    TimeoutMs = 10_000,
    Retry = new RetryOptions { MaxRetries = 2, BaseDelayMs = 300, MaxDelayMs = 5000 }, // network errors / 429 / 5xx, exponential backoff + jitter, honors Retry-After
    Transport = myCustomTransport, // bring your own HTTP stack — see HTTP transport below
};
```

### Events

```csharp
client.Validated += (_, result) => Console.WriteLine($"validated: {result.Valid}");
client.Retry += (_, args) => Console.WriteLine($"retrying {args.Method}, attempt {args.Attempt} in {args.DelayMs}ms");
client.Error += (_, args) => ReportToCrashlytics(args.Method, args.Exception);
```

Available events: `Validated`, `Activated`, `Deactivated`, `TokenIssued`, `Retry`, `Error`.

### Offline verification

`client.TokenAsync()` mints an EdDSA-signed JWT whose claims mirror `ValidateAsync()`'s response. Verify it fully offline (no network beyond the first JWKS fetch, cached for the process lifetime):

```csharp
using Licensr.Sdk.Offline;

var token = await client.TokenAsync(new ValidateRequest { LicenseKey = licenseKey });
var claims = await OfflineTokenVerifier.VerifyAsync(token.Token, token.JwksUrl);
// claims.Valid, claims.Entitlement, claims.Exp, ...
```

Offline tokens carry no revocation signal — they prove the token was genuinely issued and hasn't expired, not that the license is still active _right now_. Re-validate online before `Exp`.

To boot fully offline (no network at all, e.g. on first launch before any successful `TokenAsync` call), persist the last-known-good token yourself using the `IOfflineTokenStore` interface (`InMemoryOfflineTokenStore` is included, but doesn't survive a restart — back it with a config file, `PlayerPrefs`, or your platform's keychain):

```csharp
var store = new InMemoryOfflineTokenStore(); // swap for your own persistent implementation

var cached = store.Get(licenseKey);
if (cached is not null)
{
    try
    {
        await OfflineTokenVerifier.VerifyAsync(cached.Token, jwksUrl); // still valid — usable while offline
    }
    catch (LicensrTokenVerificationException)
    {
        // expired or tampered — fall through to an online check
    }
}
```

### Errors

| Exception                          | When                                                                                                                                                                        |
| ----------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `LicensrApiException`               | Non-2xx response. Has `Status`, `Code` (matches `contract/conformance.yaml`), `Message`, and `RetryAfterSeconds`. Branch on `Code`, not `Message` — the message is for humans. |
| `LicensrNetworkException`           | The request never got a response (network failure, timeout, retries exhausted).                                                                                              |
| `LicensrTokenVerificationException` | `OfflineTokenVerifier.VerifyAsync` rejected a bad signature, wrong algorithm, or an expired/not-yet-valid token.                                                              |

## Hardware/device IDs

Unlike `sdks/cpp`, this SDK doesn't compute a hardware ID itself — a general-purpose `netstandard2.1` library has no portable way to do that, and Unity already provides one:

```csharp
config.DeviceId = SystemInfo.deviceUniqueIdentifier; // Unity — reuse the same value for ActivateRequest.Identifier
```

For non-Unity .NET desktop apps, generate and persist your own stable identifier (a GUID written to a config file on first run is sufficient) and pass it as both `DeviceId` and the seat `ActivateRequest.Identifier`.

## HTTP transport

The default `HttpClientTransport` (backed by `System.Net.Http.HttpClient`) works on every Unity IL2CPP platform **except WebGL**, where `HttpClient` isn't supported. Implement `IHttpTransport` (one method, `SendAsync`) with a `UnityWebRequest`-backed transport for WebGL and pass it as `LicensrClientConfig.Transport` — see `Http/IHttpTransport.cs`.

## Unity notes

- **IL2CPP**: every (de)serialized type goes through a source-generated `JsonSerializerContext` (`LicensrJsonContext.cs`), not `System.Text.Json`'s reflection-based fallback — safe under ahead-of-time compilation. Unity's Roslyn version has supported C# source generators since 2021.2; older versions can't build this SDK from source (the prebuilt NuGet/UPM artifacts are unaffected either way).
- **`System.Text.Json` on Unity**: Unity 2021.2+ with the **.NET Standard 2.1** API compatibility level includes it; older configurations may need `System.Text.Json.dll` (and its `System.Runtime.CompilerServices.Unsafe` dependency) added manually from NuGet.
- **BouncyCastle.Cryptography**: required for offline verification and not bundled in the UPM package — see [`upm/README.md`](upm/README.md#required-dependency-bouncycastlecryptography).
- **Coroutines/UniTask**: every method returns `Task<T>`, directly awaitable from an `async` Unity method, `yield return`-able from a coroutine, or convertible via UniTask's `.AsUniTask()`.

## Development

```bash
dotnet build sdks/dotnet
dotnet test sdks/dotnet
dotnet format sdks/dotnet
```

### Conformance suite

`Licensr.Sdk.Tests/ConformanceTests.cs` runs every case in [`contract/conformance.yaml`](../../contract/README.md) against a real, running backend — the same contract `backend/tests/test_conformance_contract.py` and `sdks/js`/`sdks/cpp`'s own runners enforce. It's skipped by default (no live backend in a normal `dotnet test` run); to run it locally:

```bash
# 1. Start Postgres + migrate (from repo root)
docker compose up -d postgres
cd backend && APP_ENV=dev poetry run alembic upgrade head

# 2. Start the backend (BILLING_PROVIDER=mock avoids needing real Stripe creds)
APP_ENV=dev BILLING_PROVIDER=mock poetry run uvicorn main:app --port 8080

# 3. Seed fixtures for every case (in another shell)
poetry run python scripts/seed_conformance_fixtures.py --out /tmp/conformance_fixtures.json

# 4. Run the conformance suite (from sdks/dotnet)
cd ../sdks/dotnet
CONFORMANCE_BASE_URL=http://localhost:8080 \
CONFORMANCE_FIXTURES_PATH=/tmp/conformance_fixtures.json \
dotnet test --filter "FullyQualifiedName~ConformanceTests"
```

The `rate_limit_exceeded` case additionally requires `CONFORMANCE_RUN_SLOW=1` (it fires 61 requests to trip the bucket) — a plain local run skips just that one case. See `.github/workflows/test-sdk-dotnet.yaml` for how CI wires up the same four steps as one job.

## Releasing

Bump `<Version>` in `Licensr.Sdk.csproj` **and** `"version"` in `upm/package.json`, run `scripts/sync-upm.sh`, commit to `main`, then tag `dotnet/vX.Y.Z` and push the tag (triggers NuGet publish + mirror). Step-by-step: [`docs/SDK_PROGRAM.md` §2](../../docs/SDK_PROGRAM.md#2-releasing-an-sdk-update) or [`sdks/RELEASING.md`](../RELEASING.md).

## Unity UPM package

[`upm/`](upm/) is a self-contained [UPM package](https://docs.unity3d.com/Manual/cus-layout.html) — `package.json`, an `.asmdef`, and a `Runtime/` folder that's an exact copy of this project's source (no Unity-only code, no `#if UNITY` branches needed). Run `scripts/sync-upm.sh` after changing anything under `Licensr.Sdk/` and commit the result — CI (`test-sdk-dotnet.yaml`) fails the build if the two drift.
