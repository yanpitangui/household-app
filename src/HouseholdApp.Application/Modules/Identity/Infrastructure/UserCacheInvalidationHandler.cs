using HouseholdApp.Application.Modules.Identity.Domain;
using HouseholdApp.Application.Shared.Events;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.Application.Modules.Identity.Infrastructure;

internal sealed class UserCacheInvalidationHandler(IFusionCache cache) : IEventHandler<UserProvisioned>, IEventHandler<UserLocaleChanged>
{
    public Task HandleAsync(UserProvisioned evt, CancellationToken ct = default) => RemoveAsync(evt.UserId, evt.Subject, ct);

    public Task HandleAsync(UserLocaleChanged evt, CancellationToken ct = default) => RemoveAsync(evt.UserId, evt.Subject, ct);

    private async Task RemoveAsync(Guid userId, string subject, CancellationToken ct)
    {
        await cache.RemoveAsync(UserCacheKeys.ById(userId), token: ct);
        await cache.RemoveAsync(UserCacheKeys.BySubject(subject), token: ct);
    }
}
