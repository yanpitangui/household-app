using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;
using HouseholdApp.Application.Shared.Events;

namespace HouseholdApp.Application.Modules.Integrations.Infrastructure;

// Valtuutus already denies a former member; revoking keeps their token list honest too.
public sealed class RevokeAccessTokensOnMemberRemoved(IAccessTokenRepository tokens) : IEventHandler<HouseholdMemberRemoved>
{
    public Task HandleAsync(HouseholdMemberRemoved evt, CancellationToken ct = default) =>
        tokens.RevokeAllAsync(evt.UserId, evt.HouseholdId, evt.OccurredAt, ct);
}
