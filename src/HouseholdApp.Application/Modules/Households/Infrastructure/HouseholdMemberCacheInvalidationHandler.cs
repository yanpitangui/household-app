using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Shared.Events;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.Application.Modules.Households.Infrastructure;

internal sealed class HouseholdMemberCacheInvalidationHandler(IFusionCache cache)
    : IEventHandler<HouseholdMemberJoined>,
      IEventHandler<HouseholdMemberRemoved>,
      IEventHandler<HouseholdRoleChanged>
{
    public async Task HandleAsync(HouseholdMemberJoined evt, CancellationToken ct) =>
        await cache.RemoveAsync(HouseholdCacheKeys.Members(evt.HouseholdId), token: ct);

    public async Task HandleAsync(HouseholdMemberRemoved evt, CancellationToken ct) =>
        await cache.RemoveAsync(HouseholdCacheKeys.Members(evt.HouseholdId), token: ct);

    public async Task HandleAsync(HouseholdRoleChanged evt, CancellationToken ct) =>
        await cache.RemoveAsync(HouseholdCacheKeys.Members(evt.HouseholdId), token: ct);
}
