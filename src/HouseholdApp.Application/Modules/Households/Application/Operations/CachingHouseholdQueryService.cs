using HouseholdApp.Application.Modules.Households.Application.Ports;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.Application.Modules.Households.Application.Operations;

internal sealed class CachingHouseholdQueryService(IHouseholdQueries inner, IFusionCache cache) : IHouseholdQueries
{
    private static readonly FusionCacheEntryOptions EntryOptions = new() { Duration = TimeSpan.FromDays(1) };

    public async Task<IReadOnlyList<HouseholdSummary>> ListForUserAsync(Guid userId, CancellationToken ct = default) =>
        await cache.GetOrSetAsync<IReadOnlyList<HouseholdSummary>>(
            HouseholdCacheKeys.ListForUser(userId),
            token => inner.ListForUserAsync(userId, token),
            EntryOptions,
            token: ct);

    public async Task<IReadOnlyList<HouseholdName>> ListNamesAsync(Guid userId, CancellationToken ct = default) =>
        await cache.GetOrSetAsync<IReadOnlyList<HouseholdName>>(
            HouseholdCacheKeys.ListNames(userId),
            token => inner.ListNamesAsync(userId, token),
            EntryOptions,
            token: ct);

    public async Task<HouseholdDetail?> GetAsync(Guid householdId, CancellationToken ct = default) =>
        await cache.GetOrSetAsync<HouseholdDetail?>(
            HouseholdCacheKeys.Detail(householdId),
            token => inner.GetAsync(householdId, token),
            EntryOptions,
            token: ct);

    public async Task<IReadOnlyList<HouseholdMemberDto>> GetMembersAsync(Guid householdId, CancellationToken ct = default) =>
        await cache.GetOrSetAsync<IReadOnlyList<HouseholdMemberDto>>(
            HouseholdCacheKeys.Members(householdId),
            token => inner.GetMembersAsync(householdId, token),
            EntryOptions,
            token: ct);
}
