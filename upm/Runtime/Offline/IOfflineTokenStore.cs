namespace Licensr.Sdk.Offline;

public sealed class CachedOfflineToken
{
    public required string Token { get; init; }

    public required OfflineTokenClaims Claims { get; init; }

    /// <summary><see cref="DateTimeOffset.UtcNow"/> at the time this entry was cached.</summary>
    public required DateTimeOffset CachedAt { get; init; }
}

/// <summary>
/// Pluggable persistence for the last-known-good offline token per license,
/// so an app can boot fully offline (no network at all) before its first
/// successful <c>TokenAsync</c> call in a given session. The default
/// <see cref="InMemoryOfflineTokenStore"/> does not persist across process
/// restarts — provide your own (backed by a config file, <c>PlayerPrefs</c>,
/// OS keychain, etc.) for that.
/// </summary>
public interface IOfflineTokenStore
{
    CachedOfflineToken? Get(string licenseKey);

    void Set(string licenseKey, CachedOfflineToken entry);

    void Clear(string licenseKey);
}

public sealed class InMemoryOfflineTokenStore : IOfflineTokenStore
{
    private readonly Dictionary<string, CachedOfflineToken> _entries = new();

    public CachedOfflineToken? Get(string licenseKey) => _entries.TryGetValue(licenseKey, out var entry) ? entry : null;

    public void Set(string licenseKey, CachedOfflineToken entry) => _entries[licenseKey] = entry;

    public void Clear(string licenseKey) => _entries.Remove(licenseKey);
}
