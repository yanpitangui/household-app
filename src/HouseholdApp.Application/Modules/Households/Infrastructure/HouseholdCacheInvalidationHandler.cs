using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Shared.Events;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.Application.Modules.Households.Infrastructure;

internal sealed class HouseholdCacheInvalidationHandler(IFusionCache cache)
    : IEventHandler<HouseholdCreated>,
      IEventHandler<HouseholdMemberJoined>,
      IEventHandler<HouseholdMemberRemoved>,
      IEventHandler<HouseholdRoleChanged>
{
    public async Task HandleAsync(HouseholdCreated evt, CancellationToken ct = default)
    {
        await cache.RemoveAsync(HouseholdCacheKeys.ListForUser(evt.OwnerId), token: ct);
        await cache.RemoveAsync(HouseholdCacheKeys.ListNames(evt.OwnerId), token: ct);
    }

    public async Task HandleAsync(HouseholdMemberJoined evt, CancellationToken ct = default)
    {
        await cache.RemoveAsync(HouseholdCacheKeys.Members(evt.HouseholdId), token: ct);
        await cache.RemoveAsync(HouseholdCacheKeys.Detail(evt.HouseholdId), token: ct);
        await cache.RemoveAsync(HouseholdCacheKeys.ListForUser(evt.UserId), token: ct);
        await cache.RemoveAsync(HouseholdCacheKeys.ListNames(evt.UserId), token: ct);
    }

    public async Task HandleAsync(HouseholdMemberRemoved evt, CancellationToken ct = default)
    {
        await cache.RemoveAsync(HouseholdCacheKeys.Members(evt.HouseholdId), token: ct);
        await cache.RemoveAsync(HouseholdCacheKeys.Detail(evt.HouseholdId), token: ct);
        await cache.RemoveAsync(HouseholdCacheKeys.ListForUser(evt.UserId), token: ct);
        await cache.RemoveAsync(HouseholdCacheKeys.ListNames(evt.UserId), token: ct);
    }

    public async Task HandleAsync(HouseholdRoleChanged evt, CancellationToken ct = default)
    {
        await cache.RemoveAsync(HouseholdCacheKeys.Members(evt.HouseholdId), token: ct);
        await cache.RemoveAsync(HouseholdCacheKeys.Detail(evt.HouseholdId), token: ct);
    }
}
