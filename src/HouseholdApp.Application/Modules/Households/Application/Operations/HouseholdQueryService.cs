using Dapper;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Modules.Identity.Application.Ports;
using Npgsql;

namespace HouseholdApp.Application.Modules.Households.Application.Operations;

internal sealed record HouseholdDetailRow(Guid Id, string Name, DateTime CreatedAt);
internal sealed record HouseholdMemberRoleRow(Guid UserId, short Role);

public sealed class HouseholdQueryService(
    NpgsqlDataSource db,
    IUserQuery userQuery) : IHouseholdQueries
{
    public async Task<IReadOnlyList<HouseholdSummary>> ListForUserAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<HouseholdSummary>(
            """
            SELECT h.id, h.name, COUNT(m.user_id) AS MemberCount, h.created_at AS CreatedAt
            FROM households.households h
            JOIN households.members m ON m.household_id = h.id
            WHERE h.id IN (
                SELECT household_id FROM households.members WHERE user_id = @userId
            )
            GROUP BY h.id, h.name, h.created_at
            ORDER BY h.created_at DESC
            """,
            new { userId });
        return rows.ToList();
    }

    public async Task<HouseholdDetail?> GetAsync(Guid householdId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);

        var household = await conn.QuerySingleOrDefaultAsync<HouseholdDetailRow>(
            "SELECT id, name, created_at AS CreatedAt FROM households.households WHERE id = @householdId",
            new { householdId });

        if (household is null) return null;

        var members = await conn.QueryAsync<HouseholdMemberRoleRow>(
            "SELECT user_id AS UserId, role FROM households.members WHERE household_id = @householdId",
            new { householdId });

        var memberList = members.ToList();
        var userIds = memberList.Select(m => m.UserId).ToList();
        var profiles = await userQuery.GetByIdsAsync(userIds, ct);
        var profileMap = profiles.ToDictionary(p => p.Id);

        var memberDtos = memberList
            .Select(m => new HouseholdMemberDto(
                m.UserId,
                profileMap.TryGetValue(m.UserId, out var p) ? p.DisplayName : "Unknown",
                ((HouseholdRole)m.Role).ToString()))
            .ToList();

        return new HouseholdDetail(household.Id, household.Name, household.CreatedAt, memberDtos);
    }
}
