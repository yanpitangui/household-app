using Valtuutus.Core.Engines.Check;
using Valtuutus.Lang;

namespace HouseholdApp.Application.Shared.Authorization;

public sealed class ValtuutusHouseholdGuard(ICheckEngine checkEngine) : IHouseholdGuard
{
    private static readonly Dictionary<string, HouseholdPermission> ByName = new()
    {
        [SchemaConstsGen.Household.Permissions.View] = HouseholdPermission.View,
        [SchemaConstsGen.Household.Permissions.Manage] = HouseholdPermission.Manage,
        [SchemaConstsGen.Household.Permissions.ManageRoles] = HouseholdPermission.ManageRoles,
        [SchemaConstsGen.Household.Permissions.EditSettings] = HouseholdPermission.EditSettings,
        [SchemaConstsGen.Household.Permissions.Invite] = HouseholdPermission.Invite,
        [SchemaConstsGen.Household.Permissions.Leave] = HouseholdPermission.Leave,
        [SchemaConstsGen.Household.Permissions.TransferOwnership] = HouseholdPermission.TransferOwnership,
    };

    public Task<bool> IsMemberAsync(Guid householdId, Guid userId, CancellationToken ct = default) =>
        CheckAsync(householdId, userId, SchemaConstsGen.Household.Permissions.View, ct);

    public Task<bool> CanManageAsync(Guid householdId, Guid userId, CancellationToken ct = default) =>
        CheckAsync(householdId, userId, SchemaConstsGen.Household.Permissions.Manage, ct);

    public Task<bool> CanManageRolesAsync(Guid householdId, Guid userId, CancellationToken ct = default) =>
        CheckAsync(householdId, userId, SchemaConstsGen.Household.Permissions.ManageRoles, ct);

    public Task<bool> HasAsync(Guid householdId, Guid userId, HouseholdPermission permission, CancellationToken ct = default) =>
        CheckAsync(householdId, userId, ByName.Single(kv => kv.Value == permission).Key, ct);

    public async Task<HouseholdPermissions> GetPermissionsAsync(Guid householdId, Guid userId, CancellationToken ct = default)
    {
        var result = await checkEngine.SubjectPermission(new SubjectPermissionRequest
        {
            EntityType = SchemaConstsGen.Household.Name,
            EntityId = householdId.ToString(),
            SubjectType = SchemaConstsGen.User.Name,
            SubjectId = userId.ToString()
        }, ct);
        return new HouseholdPermissions(result.Permissions
            .Where(p => p.Value && ByName.ContainsKey(p.Key))
            .Select(p => ByName[p.Key]).ToHashSet());
    }

    // Target rules live here because Valtuutus's check cache keys ignore request context, so a
    // context-dependent permission would return the first target's answer for every target.
    public async Task<bool> CanRemoveMemberAsync(Guid householdId, Guid userId, Guid targetUserId, string targetRole, CancellationToken ct = default) =>
        userId != targetUserId && !IsOwnerRole(targetRole) && await CanManageAsync(householdId, userId, ct);

    public async Task<bool> CanChangeRoleAsync(Guid householdId, Guid userId, string targetRole, CancellationToken ct = default) =>
        !IsOwnerRole(targetRole) && await CanManageRolesAsync(householdId, userId, ct);

    private static bool IsOwnerRole(string role) => string.Equals(role, "owner", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> CheckAsync(Guid householdId, Guid userId, string permission, CancellationToken ct) =>
        (await checkEngine.Check(new CheckRequest(
            SchemaConstsGen.Household.Name, householdId.ToString(), permission,
            SchemaConstsGen.User.Name, userId.ToString()), ct)).Allowed;
}
