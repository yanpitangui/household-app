namespace HouseholdApp.Application.Shared.Authorization;

public enum HouseholdPermission { View, Manage, ManageRoles, EditSettings, Invite, Leave, TransferOwnership }

/// <summary>Household-wide permissions of one user; target-scoped ones are asked per member.</summary>
public sealed record HouseholdPermissions(IReadOnlySet<HouseholdPermission> Granted)
{
    public static readonly HouseholdPermissions None = new(new HashSet<HouseholdPermission>());
    public bool Can(HouseholdPermission permission) => Granted.Contains(permission);
}

public interface IHouseholdGuard
{
    Task<bool> IsMemberAsync(Guid householdId, Guid userId, CancellationToken ct = default);
    Task<bool> CanManageAsync(Guid householdId, Guid userId, CancellationToken ct = default);
    Task<bool> CanManageRolesAsync(Guid householdId, Guid userId, CancellationToken ct = default);
    Task<bool> HasAsync(Guid householdId, Guid userId, HouseholdPermission permission, CancellationToken ct = default);
    Task<HouseholdPermissions> GetPermissionsAsync(Guid householdId, Guid userId, CancellationToken ct = default);
    Task<bool> CanRemoveMemberAsync(Guid householdId, Guid userId, Guid targetUserId, string targetRole, CancellationToken ct = default);
    Task<bool> CanChangeRoleAsync(Guid householdId, Guid userId, string targetRole, CancellationToken ct = default);
}
