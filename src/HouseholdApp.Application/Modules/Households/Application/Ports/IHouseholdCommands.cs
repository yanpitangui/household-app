namespace HouseholdApp.Application.Modules.Households.Application.Ports;

public interface IHouseholdCommands
{
    Task<Guid> CreateAsync(string name, CancellationToken ct = default);
    Task<string> InviteAsync(Guid householdId, CancellationToken ct = default);
    Task<bool> AcceptInvitationAsync(string token, CancellationToken ct = default);
    Task RevokeInvitationAsync(Guid householdId, Guid invitationId, CancellationToken ct = default);
    Task RemoveMemberAsync(Guid householdId, Guid targetUserId, CancellationToken ct = default);
    Task ChangeMemberRoleAsync(Guid householdId, Guid targetUserId, string newRole, CancellationToken ct = default);
}
