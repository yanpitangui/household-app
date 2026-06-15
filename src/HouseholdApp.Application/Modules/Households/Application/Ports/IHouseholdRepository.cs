using HouseholdApp.Application.Modules.Households.Domain;

namespace HouseholdApp.Application.Modules.Households.Application.Ports;

public interface IHouseholdRepository
{
    Task SaveAsync(Household household, CancellationToken ct = default);
    Task SaveInvitationAsync(HouseholdInvitation invitation, CancellationToken ct = default);
    Task<Household?> GetAsync(Guid householdId, CancellationToken ct = default);
    Task<HouseholdInvitation?> GetInvitationByTokenAsync(string token, CancellationToken ct = default);
    Task<HouseholdInvitation?> GetInvitationAsync(Guid invitationId, CancellationToken ct = default);
    Task<bool> ConsumeInvitationAtomicAsync(string token, Guid userId, DateTimeOffset now, CancellationToken ct = default);
}
