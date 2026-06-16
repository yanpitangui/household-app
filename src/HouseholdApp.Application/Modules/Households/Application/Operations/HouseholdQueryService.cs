using Dapper;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Modules.Households.Domain;
using Npgsql;

namespace HouseholdApp.Application.Modules.Households.Application.Operations;

internal sealed record HouseholdDetailRow(Guid Id, string Name, DateTime CreatedAt);

public sealed class HouseholdQueryService(NpgsqlDataSource db) : IHouseholdQueries
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

    public async Task<IReadOnlyList<HouseholdName>> ListNamesAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<HouseholdName>(
            """
            SELECT h.id, h.name
            FROM households.households h
            WHERE h.id IN (
                SELECT household_id FROM households.members WHERE user_id = @userId
            )
            ORDER BY h.name
            """,
            new { userId });
        return rows.ToList();
    }

    public async Task<HouseholdDetail?> GetAsync(Guid householdId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var multi = await conn.QueryMultipleAsync(
            """
            SELECT id, name, created_at AS CreatedAt
            FROM households.households
            WHERE id = @householdId;

            SELECT m.user_id AS UserId, u.display_name AS DisplayName,
                   CASE m.role WHEN 2 THEN 'Owner' WHEN 1 THEN 'Admin' ELSE 'Member' END AS Role,
                   u.picture_url AS PictureUrl
            FROM households.members m
            JOIN identity.users u ON u.id = m.user_id
            WHERE m.household_id = @householdId
            """,
            new { householdId });

        var household = await multi.ReadSingleOrDefaultAsync<HouseholdDetailRow>();
        if (household is null) return null;

        var members = (await multi.ReadAsync<HouseholdMemberDto>()).ToList();
        return new HouseholdDetail(household.Id, household.Name, household.CreatedAt, members);
    }

    public async Task<IReadOnlyList<HouseholdMemberDto>> GetMembersAsync(Guid householdId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<HouseholdMemberDto>(
            """
            SELECT m.user_id AS UserId, u.display_name AS DisplayName,
                   CASE m.role WHEN 2 THEN 'Owner' WHEN 1 THEN 'Admin' ELSE 'Member' END AS Role,
                   u.picture_url AS PictureUrl
            FROM households.members m
            JOIN identity.users u ON u.id = m.user_id
            WHERE m.household_id = @householdId
            """,
            new { householdId });
        return rows.ToList();
    }
}
