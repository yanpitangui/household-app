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

    // Target-scoped rules are schema permissions evaluated over request context; Valtuutus 2.3.1
    // keys cached results by that context, so each target gets its own answer.
    public Task<bool> CanRemoveMemberAsync(Guid householdId, Guid userId, Guid targetUserId, string targetRole, CancellationToken ct = default) =>
        CheckAsync(householdId, userId, SchemaConstsGen.Household.Permissions.RemoveMember, ct, new Dictionary<string, object>
        {
            ["target_role"] = targetRole.ToLowerInvariant(),
            ["target_id"] = targetUserId.ToString(),
            ["subject_id"] = userId.ToString()
        });

    public Task<bool> CanChangeRoleAsync(Guid householdId, Guid userId, string targetRole, CancellationToken ct = default) =>
        CheckAsync(householdId, userId, SchemaConstsGen.Household.Permissions.ChangeRole, ct, new Dictionary<string, object>
        {
            ["target_role"] = targetRole.ToLowerInvariant()
        });

    private async Task<bool> CheckAsync(Guid householdId, Guid userId, string permission, CancellationToken ct,
        Dictionary<string, object>? context = null) =>
        (await checkEngine.Check(new CheckRequest(
            SchemaConstsGen.Household.Name, householdId.ToString(), permission,
            SchemaConstsGen.User.Name, userId.ToString())
        {
            Context = context ?? new Dictionary<string, object>()
        }, ct)).Allowed;
}
