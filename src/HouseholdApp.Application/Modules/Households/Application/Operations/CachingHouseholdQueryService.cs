using HouseholdApp.Application.Modules.Households.Application.Ports;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.Application.Modules.Households.Application.Operations;

internal sealed class CachingHouseholdQueryService(IHouseholdQueries inner, IFusionCache cache) : IHouseholdQueries
{
    public Task<IReadOnlyList<HouseholdSummary>> ListForUserAsync(Guid userId, CancellationToken ct = default) =>
        inner.ListForUserAsync(userId, ct);

    public Task<IReadOnlyList<HouseholdName>> ListNamesAsync(Guid userId, CancellationToken ct = default) =>
        inner.ListNamesAsync(userId, ct);

    public Task<HouseholdDetail?> GetAsync(Guid householdId, CancellationToken ct = default) =>
        inner.GetAsync(householdId, ct);

    public async Task<IReadOnlyList<HouseholdMemberDto>> GetMembersAsync(Guid householdId, CancellationToken ct = default) =>
        await cache.GetOrSetAsync<IReadOnlyList<HouseholdMemberDto>>(
            HouseholdCacheKeys.Members(householdId),
            token => inner.GetMembersAsync(householdId, token),
            new FusionCacheEntryOptions { Duration = TimeSpan.FromDays(1) },
            token: ct);
}
