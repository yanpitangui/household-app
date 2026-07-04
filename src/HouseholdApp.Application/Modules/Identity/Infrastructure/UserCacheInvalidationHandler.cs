using HouseholdApp.Application.Modules.Identity.Domain;
using HouseholdApp.Application.Shared.Events;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.Application.Modules.Identity.Infrastructure;

internal sealed class UserCacheInvalidationHandler(IFusionCache cache) : IEventHandler<UserProvisioned>
{
    public async Task HandleAsync(UserProvisioned evt, CancellationToken ct = default)
    {
        await cache.RemoveAsync(UserCacheKeys.ById(evt.UserId), token: ct);
        await cache.RemoveAsync(UserCacheKeys.BySubject(evt.Subject), token: ct);
    }
}
