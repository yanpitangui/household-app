namespace HouseholdApp.Application.Shared.Authorization;

public interface IHouseholdGuard
{
    Task<bool> IsMemberAsync(Guid householdId, Guid userId, CancellationToken ct = default);
    Task<bool> CanManageAsync(Guid householdId, Guid userId, CancellationToken ct = default);
    Task<bool> CanManageRolesAsync(Guid householdId, Guid userId, CancellationToken ct = default);
}
