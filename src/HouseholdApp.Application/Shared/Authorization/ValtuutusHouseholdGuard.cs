using Valtuutus.Core.Engines.Check;
using Valtuutus.Lang;

namespace HouseholdApp.Application.Shared.Authorization;

public sealed class ValtuutusHouseholdGuard(ICheckEngine checkEngine) : IHouseholdGuard
{
    public Task<bool> IsMemberAsync(Guid householdId, Guid userId, CancellationToken ct = default) =>
        checkEngine.Check(new CheckRequest(
            SchemaConstsGen.Household.Name, householdId.ToString(),
            SchemaConstsGen.Household.Permissions.View,
            SchemaConstsGen.User.Name, userId.ToString()), ct);

    public Task<bool> CanManageAsync(Guid householdId, Guid userId, CancellationToken ct = default) =>
        checkEngine.Check(new CheckRequest(
            SchemaConstsGen.Household.Name, householdId.ToString(),
            SchemaConstsGen.Household.Permissions.Manage,
            SchemaConstsGen.User.Name, userId.ToString()), ct);

    public Task<bool> CanManageRolesAsync(Guid householdId, Guid userId, CancellationToken ct = default) =>
        checkEngine.Check(new CheckRequest(
            SchemaConstsGen.Household.Name, householdId.ToString(),
            SchemaConstsGen.Household.Permissions.ManageRoles,
            SchemaConstsGen.User.Name, userId.ToString()), ct);
}
