using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Caching;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.Application.Modules.Households.Application.Operations;

internal sealed class CachingHouseholdQueryService(IHouseholdQueries inner, IFusionCache cache, TimeProvider time)
    : IHouseholdQueries, IHouseholdQueriesWithLastModified
{
    private static readonly FusionCacheEntryOptions EntryOptions = new() { Duration = TimeSpan.FromDays(1) };

    public async Task<IReadOnlyList<HouseholdSummary>> ListForUserAsync(Guid userId, CancellationToken ct = default) =>
        (await GetOrSetListForUserAsync(userId, ct)).Value;

    public Task<WithLastModified<IReadOnlyList<HouseholdSummary>>> ListForUserWithLastModifiedAsync(Guid userId, CancellationToken ct = default) =>
        GetOrSetListForUserAsync(userId, ct);

    public async Task<IReadOnlyList<HouseholdName>> ListNamesAsync(Guid userId, CancellationToken ct = default) =>
        (await GetOrSetListNamesAsync(userId, ct)).Value;

    public Task<WithLastModified<IReadOnlyList<HouseholdName>>> ListNamesWithLastModifiedAsync(Guid userId, CancellationToken ct = default) =>
        GetOrSetListNamesAsync(userId, ct);

    public async Task<HouseholdDetail?> GetAsync(Guid householdId, CancellationToken ct = default) =>
        (await GetOrSetDetailAsync(householdId, ct)).Value;

    public Task<WithLastModified<HouseholdDetail?>> GetWithLastModifiedAsync(Guid householdId, CancellationToken ct = default) =>
        GetOrSetDetailAsync(householdId, ct);

    public async Task<IReadOnlyList<HouseholdMemberDto>> GetMembersAsync(Guid householdId, CancellationToken ct = default) =>
        await cache.GetOrSetAsync<IReadOnlyList<HouseholdMemberDto>>(
            HouseholdCacheKeys.Members(householdId),
            token => inner.GetMembersAsync(householdId, token),
            EntryOptions,
            token: ct);

    private Task<WithLastModified<IReadOnlyList<HouseholdSummary>>> GetOrSetListForUserAsync(Guid userId, CancellationToken ct) =>
        cache.GetOrSetAsync<WithLastModified<IReadOnlyList<HouseholdSummary>>>(
            HouseholdCacheKeys.ListForUser(userId),
            async token => new WithLastModified<IReadOnlyList<HouseholdSummary>>(
                await inner.ListForUserAsync(userId, token),
                time.GetUtcNow()),
            EntryOptions,
            token: ct).AsTask();

    private Task<WithLastModified<HouseholdDetail?>> GetOrSetDetailAsync(Guid householdId, CancellationToken ct) =>
        cache.GetOrSetAsync<WithLastModified<HouseholdDetail?>>(
            HouseholdCacheKeys.Detail(householdId),
            async token => new WithLastModified<HouseholdDetail?>(
                await inner.GetAsync(householdId, token),
                time.GetUtcNow()),
            EntryOptions,
            token: ct).AsTask();

    private Task<WithLastModified<IReadOnlyList<HouseholdName>>> GetOrSetListNamesAsync(Guid userId, CancellationToken ct) =>
        cache.GetOrSetAsync<WithLastModified<IReadOnlyList<HouseholdName>>>(
            HouseholdCacheKeys.ListNames(userId),
            async token => new WithLastModified<IReadOnlyList<HouseholdName>>(
                await inner.ListNamesAsync(userId, token),
                time.GetUtcNow()),
            EntryOptions,
            token: ct).AsTask();
}
