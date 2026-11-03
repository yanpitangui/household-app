using Dapper;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Shared.Persistence;

namespace HouseholdApp.Application.Modules.Households.Infrastructure;

internal sealed record HouseholdRow(Guid Id, string Name, string TimeZone, DateTimeOffset CreatedAt);
internal sealed record HouseholdMemberRow(Guid HouseholdId, Guid UserId, short Role);

internal sealed class HouseholdRepository(IUnitOfWork uow) : IHouseholdRepository
{
    public async Task InsertAsync(Household household, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        await conn.ExecuteAsync(
            "INSERT INTO households.households (id, name, time_zone, created_at) VALUES (@Id, @Name, @TimeZone, @CreatedAt)",
            new { household.Id, household.Name, household.TimeZone, household.CreatedAt }, uow.CurrentTransaction);
        foreach (var m in household.Members)
            await AddMemberAsync(household.Id, m.UserId, m.Role, ct);
    }

    public async Task RenameAsync(Guid householdId, string name, CancellationToken ct = default) =>
        await Execute("UPDATE households.households SET name = @name WHERE id = @householdId", new { householdId, name }, ct);

    public async Task SetTimeZoneAsync(Guid householdId, string timeZone, CancellationToken ct = default) =>
        await Execute("UPDATE households.households SET time_zone = @timeZone WHERE id = @householdId", new { householdId, timeZone }, ct);

    // A rejoining Former Member gets a fresh row; the upsert keeps a concurrent double-accept harmless.
    public async Task AddMemberAsync(Guid householdId, Guid userId, HouseholdRole role, CancellationToken ct = default) =>
        await Execute(
            """
            INSERT INTO households.members (household_id, user_id, role) VALUES (@householdId, @userId, @role)
            ON CONFLICT (household_id, user_id) DO UPDATE SET role = EXCLUDED.role
            """, new { householdId, userId, role = (int)role }, ct);

    public async Task RemoveMemberAsync(Guid householdId, Guid userId, CancellationToken ct = default) =>
        await Execute("DELETE FROM households.members WHERE household_id = @householdId AND user_id = @userId", new { householdId, userId }, ct);

    public async Task SetRoleAsync(Guid householdId, Guid userId, HouseholdRole role, CancellationToken ct = default) =>
        await Execute("UPDATE households.members SET role = @role WHERE household_id = @householdId AND user_id = @userId",
            new { householdId, userId, role = (int)role }, ct);

    private async Task Execute(string sql, object args, CancellationToken ct)
    {
        var conn = await uow.GetConnectionAsync(ct);
        await conn.ExecuteAsync(sql, args, uow.CurrentTransaction);
    }

    public async Task SaveInvitationAsync(HouseholdInvitation invitation, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO households.invitations (id, household_id, token, created_by, created_at, expires_at, status, accepted_by, accepted_at)
            VALUES (@Id, @HouseholdId, @Token, @CreatedBy, @CreatedAt, @ExpiresAt, @Status, @AcceptedBy, @AcceptedAt)
            ON CONFLICT (id) DO UPDATE
                SET status = EXCLUDED.status,
                    accepted_by = EXCLUDED.accepted_by,
                    accepted_at = EXCLUDED.accepted_at
            """,
            new
            {
                invitation.Id,
                invitation.HouseholdId,
                invitation.Token,
                invitation.CreatedBy,
                invitation.CreatedAt,
                invitation.ExpiresAt,
                Status = (int)invitation.Status,
                invitation.AcceptedBy,
                invitation.AcceptedAt
            },
            uow.CurrentTransaction);
    }

    public async Task<HouseholdInvitation?> GetInvitationByTokenAsync(string token, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<HouseholdInvitation>(
            """
            SELECT id, household_id AS HouseholdId, token, created_by AS CreatedBy,
                   created_at AS CreatedAt, expires_at AS ExpiresAt,
                   status, accepted_by AS AcceptedBy, accepted_at AS AcceptedAt
            FROM households.invitations
            WHERE token = @token
            """,
            new { token },
            uow.CurrentTransaction);
    }

    public async Task<Household?> GetAsync(Guid householdId, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        var tx = uow.CurrentTransaction;

        var row = await conn.QuerySingleOrDefaultAsync<HouseholdRow>(
            "SELECT id, name, time_zone AS TimeZone, created_at AS CreatedAt FROM households.households WHERE id = @householdId",
            new { householdId },
            tx);

        if (row is null) return null;

        var members = await conn.QueryAsync<HouseholdMemberRow>(
            "SELECT household_id AS HouseholdId, user_id AS UserId, role FROM households.members WHERE household_id = @householdId",
            new { householdId },
            tx);

        return Household.Reconstitute(row.Id, row.Name, row.TimeZone, row.CreatedAt,
            members.Select(m => new HouseholdMember(m.HouseholdId, m.UserId, (HouseholdRole)m.Role)).ToList());
    }

    public async Task<HouseholdInvitation?> GetInvitationAsync(Guid invitationId, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<HouseholdInvitation>(
            """
            SELECT id, household_id AS HouseholdId, token, created_by AS CreatedBy,
                   created_at AS CreatedAt, expires_at AS ExpiresAt,
                   status, accepted_by AS AcceptedBy, accepted_at AS AcceptedAt
            FROM households.invitations
            WHERE id = @invitationId
            """,
            new { invitationId },
            uow.CurrentTransaction);
    }

    public async Task<bool> ConsumeInvitationAtomicAsync(string token, Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        var rows = await conn.ExecuteAsync(
            """
            UPDATE households.invitations
            SET status = 1, accepted_by = @userId, accepted_at = @now
            WHERE token = @token AND status = 0 AND expires_at > @now
            """,
            new { token, userId, now },
            uow.CurrentTransaction);
        return rows == 1;
    }
}
