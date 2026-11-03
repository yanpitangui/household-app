using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Shared.Events;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.Application.Modules.Households.Infrastructure;

internal sealed class HouseholdCacheInvalidationHandler(IFusionCache cache)
    : IEventHandler<HouseholdCreated>,
      IEventHandler<HouseholdMemberJoined>,
      IEventHandler<HouseholdMemberRemoved>,
      IEventHandler<HouseholdRoleChanged>,
      IEventHandler<HouseholdRenamed>,
      IEventHandler<HouseholdTimeZoneChanged>
{
    public async Task HandleAsync(HouseholdCreated evt, CancellationToken ct = default)
    {
        await cache.RemoveAsync(HouseholdCacheKeys.ListForUser(evt.OwnerId), token: ct);
        await cache.RemoveAsync(HouseholdCacheKeys.ListNames(evt.OwnerId), token: ct);
    }

    public Task HandleAsync(HouseholdMemberJoined evt, CancellationToken ct = default) =>
        MembershipChangedAsync(evt.HouseholdId, evt.UserId, evt.OtherMemberIds, ct);

    public Task HandleAsync(HouseholdMemberRemoved evt, CancellationToken ct = default) =>
        MembershipChangedAsync(evt.HouseholdId, evt.UserId, evt.OtherMemberIds, ct);

    public async Task HandleAsync(HouseholdRoleChanged evt, CancellationToken ct = default)
    {
        await cache.RemoveAsync(HouseholdCacheKeys.Members(evt.HouseholdId), token: ct);
        await cache.RemoveAsync(HouseholdCacheKeys.Detail(evt.HouseholdId), token: ct);
    }

    public async Task HandleAsync(HouseholdRenamed evt, CancellationToken ct = default)
    {
        await cache.RemoveAsync(HouseholdCacheKeys.Detail(evt.HouseholdId), token: ct);
        foreach (var userId in evt.MemberIds)
        {
            await cache.RemoveAsync(HouseholdCacheKeys.ListForUser(userId), token: ct);
            await cache.RemoveAsync(HouseholdCacheKeys.ListNames(userId), token: ct);
        }
    }

    public async Task HandleAsync(HouseholdTimeZoneChanged evt, CancellationToken ct = default) =>
        await cache.RemoveAsync(HouseholdCacheKeys.Detail(evt.HouseholdId), token: ct);

    // The joining or leaving user gains or loses the household; everyone else sees its member count change.
    private async Task MembershipChangedAsync(Guid householdId, Guid userId, IReadOnlyList<Guid> otherMemberIds, CancellationToken ct)
    {
        await cache.RemoveAsync(HouseholdCacheKeys.Members(householdId), token: ct);
        await cache.RemoveAsync(HouseholdCacheKeys.Detail(householdId), token: ct);
        await cache.RemoveAsync(HouseholdCacheKeys.ListForUser(userId), token: ct);
        await cache.RemoveAsync(HouseholdCacheKeys.ListNames(userId), token: ct);
        foreach (var otherId in otherMemberIds)
            await cache.RemoveAsync(HouseholdCacheKeys.ListForUser(otherId), token: ct);
    }
}
