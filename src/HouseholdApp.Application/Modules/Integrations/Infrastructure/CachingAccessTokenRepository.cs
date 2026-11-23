using HouseholdApp.Application.Modules.Integrations.Application.Ports;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.Application.Modules.Integrations.Infrastructure;

// Caches live tokens by hash so each API request skips the database. The short record lifetime bounds how long a
// revoked token can stay usable, even when eviction cannot reach a cache node.
internal sealed class CachingAccessTokenRepository(IAccessTokenRepository inner, IFusionCache cache) : IAccessTokenRepository
{
    private static readonly FusionCacheEntryOptions RecordOptions = new() { Duration = TimeSpan.FromMinutes(1) };
    private static readonly FusionCacheEntryOptions TouchOptions = new() { Duration = TimeSpan.FromHours(1) };
    private static readonly FusionCacheEntryOptions TombstoneOptions = new() { Duration = TimeSpan.FromMinutes(10) };

    private static string HashKey(byte[] hash) => $"integrations:access-token:{Convert.ToHexStringLower(hash)}";
    private static string TouchedKey(Guid id) => $"integrations:access-token-touched:{id}";
    private static string RevokedKey(Guid id) => $"integrations:access-token-revoked:{id}";

    public Task InsertAsync(AccessTokenRecord token, CancellationToken ct = default) => inner.InsertAsync(token, ct);

    public async Task<AccessTokenRecord?> FindByHashAsync(byte[] tokenHash, CancellationToken ct = default)
    {
        var key = HashKey(tokenHash);
        var cached = await cache.TryGetAsync<AccessTokenRecord>(key, token: ct);
        if (cached.HasValue) return cached.Value;

        var record = await inner.FindByHashAsync(tokenHash, ct);
        // Unknown hashes stay out of the cache so forged or stale input cannot fill it; revoked rows need no caching.
        if (record is null || record.RevokedAt is not null) return record;

        await cache.SetAsync(key, record, RecordOptions, token: ct);
        // A lookup that raced a revoke removes its own write, so the next request reads the revoked row.
        if ((await cache.TryGetAsync<bool>(RevokedKey(record.Id), token: ct)).HasValue)
            await cache.RemoveAsync(key, token: ct);
        return record;
    }

    public Task<IReadOnlyList<AccessTokenRecord>> ListActiveAsync(Guid userId, Guid householdId, CancellationToken ct = default) =>
        inner.ListActiveAsync(userId, householdId, ct);

    public async Task<bool> RevokeAsync(Guid id, Guid userId, Guid householdId, DateTimeOffset now, CancellationToken ct = default)
    {
        var active = await inner.ListActiveAsync(userId, householdId, ct);
        var revoked = await inner.RevokeAsync(id, userId, householdId, now, ct);
        if (revoked)
            foreach (var token in active.Where(t => t.Id == id)) await Evict(token, ct);
        return revoked;
    }

    public async Task RevokeAllAsync(Guid userId, Guid householdId, DateTimeOffset now, CancellationToken ct = default)
    {
        var active = await inner.ListActiveAsync(userId, householdId, ct);
        await inner.RevokeAllAsync(userId, householdId, now, ct);
        foreach (var token in active) await Evict(token, ct);
    }

    // A marker per token keeps ordinary requests read-only; the database guard remains as a backstop.
    public async Task TouchAsync(Guid id, DateTimeOffset now, CancellationToken ct = default)
    {
        if ((await cache.TryGetAsync<bool>(TouchedKey(id), token: ct)).HasValue) return;
        await inner.TouchAsync(id, now, ct);
        await cache.SetAsync(TouchedKey(id), true, TouchOptions, token: ct);
    }

    // The tombstone goes first so a concurrent lookup that re-reads the old row can see it and clean up after itself.
    private async Task Evict(AccessTokenRecord record, CancellationToken ct)
    {
        await cache.SetAsync(RevokedKey(record.Id), true, TombstoneOptions, token: ct);
        await cache.RemoveAsync(HashKey(record.TokenHash), token: ct);
    }
}
