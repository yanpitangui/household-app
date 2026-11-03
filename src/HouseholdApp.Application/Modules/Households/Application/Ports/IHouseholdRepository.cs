using HouseholdApp.Application.Modules.Households.Domain;

namespace HouseholdApp.Application.Modules.Households.Application.Ports;

public interface IHouseholdRepository
{
    Task InsertAsync(Household household, CancellationToken ct = default);
    Task RenameAsync(Guid householdId, string name, CancellationToken ct = default);
    Task SetTimeZoneAsync(Guid householdId, string timeZone, CancellationToken ct = default);
    Task AddMemberAsync(Guid householdId, Guid userId, HouseholdRole role, CancellationToken ct = default);
    Task RemoveMemberAsync(Guid householdId, Guid userId, CancellationToken ct = default);
    Task SetRoleAsync(Guid householdId, Guid userId, HouseholdRole role, CancellationToken ct = default);
    Task SaveInvitationAsync(HouseholdInvitation invitation, CancellationToken ct = default);
    Task<Household?> GetAsync(Guid householdId, CancellationToken ct = default);
    Task<HouseholdInvitation?> GetInvitationByTokenAsync(string token, CancellationToken ct = default);
    Task<HouseholdInvitation?> GetInvitationAsync(Guid invitationId, CancellationToken ct = default);
    Task<bool> ConsumeInvitationAtomicAsync(string token, Guid userId, DateTimeOffset now, CancellationToken ct = default);
}
