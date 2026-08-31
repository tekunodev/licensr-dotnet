# Licensr SDK for Unity

This is the Unity Package Manager (UPM) distribution of [Licensr.Sdk](../README.md) — `Runtime/` is an exact, unmodified copy of that NuGet package's C# source (see [`../scripts/sync-upm.sh`](../scripts/sync-upm.sh)), wrapped in Unity package metadata (`package.json`, `Licensr.Sdk.asmdef`).

Read the [main SDK README](../README.md) for the full API, quickstart, and offline verification guide — everything there applies unchanged. This file only covers Unity-specific installation.

## Install

Package Manager → `+` → "Install package from git URL":

```
https://github.com/tekunodev/licensr-dotnet.git?path=upm
```

Or add directly to `Packages/manifest.json`:

```json
"com.licensr.sdk": "https://github.com/tekunodev/licensr-dotnet.git?path=upm"
```

## Required dependency: BouncyCastle.Cryptography

This package needs [`BouncyCastle.Cryptography`](https://www.nuget.org/packages/BouncyCastle.Cryptography) (pure managed, IL2CPP-safe) for offline EdDSA token verification — it isn't bundled here, since bundling a project-wide crypto library inside every plugin that needs one is how Unity projects end up with duplicate-assembly conflicts. Add it via one of:

- [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) → install `BouncyCastle.Cryptography` (recommended — handles updates and dedupes against any other package that also needs it).
- Manually: download the `netstandard2.0` build from [nuget.org](https://www.nuget.org/packages/BouncyCastle.Cryptography), extract `BouncyCastle.Cryptography.dll`, and drop it anywhere under `Assets/` as a plugin.

If you skip this, Unity reports a missing precompiled reference (`BouncyCastle.Cryptography.dll`) when compiling the `Licensr.Sdk` assembly — that's expected; install the dependency above to resolve it. If you only call `ValidateAsync`/`ActivateAsync`/etc. and never touch `OfflineTokenVerifier`, you still need the DLL present at compile time (C# has no "soft" reference), but never need it at runtime.

## WebGL

The bundled `HttpClientTransport` uses `System.Net.Http.HttpClient`, unsupported on WebGL. Implement `IHttpTransport` with a `UnityWebRequest`-backed transport and pass it as `LicensrClientConfig.Transport` — see the main README's [HTTP transport](../README.md#http-transport) section.
