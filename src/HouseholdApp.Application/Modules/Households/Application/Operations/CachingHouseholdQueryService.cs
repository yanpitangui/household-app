using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Caching;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.Application.Modules.Households.Application.Operations;

internal sealed class CachingHouseholdQueryService(IHouseholdQueries inner, IFusionCache cache)
    : IHouseholdQueries, IHouseholdQueriesWithETag
{
    private static readonly FusionCacheEntryOptions EntryOptions = new() { Duration = TimeSpan.FromDays(1) };

    public async Task<IReadOnlyList<HouseholdSummary>> ListForUserAsync(Guid userId, CancellationToken ct = default) =>
        (await GetOrSetListForUserAsync(userId, ct)).Value;

    public Task<WithETag<IReadOnlyList<HouseholdSummary>>> ListForUserWithETagAsync(Guid userId, CancellationToken ct = default) =>
        GetOrSetListForUserAsync(userId, ct);

    public async Task<IReadOnlyList<HouseholdName>> ListNamesAsync(Guid userId, CancellationToken ct = default) =>
        await cache.GetOrSetAsync<IReadOnlyList<HouseholdName>>(
            HouseholdCacheKeys.ListNames(userId),
            token => inner.ListNamesAsync(userId, token),
            EntryOptions,
            token: ct);

    public async Task<HouseholdDetail?> GetAsync(Guid householdId, CancellationToken ct = default) =>
        (await GetOrSetDetailAsync(householdId, ct)).Value;

    public Task<WithETag<HouseholdDetail?>> GetWithETagAsync(Guid householdId, CancellationToken ct = default) =>
        GetOrSetDetailAsync(householdId, ct);

    public async Task<IReadOnlyList<HouseholdMemberDto>> GetMembersAsync(Guid householdId, CancellationToken ct = default) =>
        await cache.GetOrSetAsync<IReadOnlyList<HouseholdMemberDto>>(
            HouseholdCacheKeys.Members(householdId),
            token => inner.GetMembersAsync(householdId, token),
            EntryOptions,
            token: ct);

    private Task<WithETag<IReadOnlyList<HouseholdSummary>>> GetOrSetListForUserAsync(Guid userId, CancellationToken ct) =>
        cache.GetOrSetAsync<WithETag<IReadOnlyList<HouseholdSummary>>>(
            HouseholdCacheKeys.ListForUser(userId),
            async token => new WithETag<IReadOnlyList<HouseholdSummary>>(
                await inner.ListForUserAsync(userId, token),
                Guid.CreateVersion7().ToString()),
            EntryOptions,
            token: ct).AsTask();

    private Task<WithETag<HouseholdDetail?>> GetOrSetDetailAsync(Guid householdId, CancellationToken ct) =>
        cache.GetOrSetAsync<WithETag<HouseholdDetail?>>(
            HouseholdCacheKeys.Detail(householdId),
            async token => new WithETag<HouseholdDetail?>(
                await inner.GetAsync(householdId, token),
                Guid.CreateVersion7().ToString()),
            EntryOptions,
            token: ct).AsTask();
}
