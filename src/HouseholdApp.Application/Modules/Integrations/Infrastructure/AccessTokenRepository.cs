using Dapper;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;
using Npgsql;

namespace HouseholdApp.Application.Modules.Integrations.Infrastructure;

public sealed class AccessTokenRepository(NpgsqlDataSource db) : IAccessTokenRepository
{
    private const string Columns = """
        id AS Id, user_id AS UserId, household_id AS HouseholdId, name AS Name, token_hash AS TokenHash,
        token_hint AS TokenHint, scopes AS Scopes, default_list_id AS DefaultListId,
        default_group_id AS DefaultGroupId, created_at AS CreatedAt, expires_at AS ExpiresAt,
        last_used_at AS LastUsedAt, revoked_at AS RevokedAt
        """;

    // Dapper maps settable properties for text[] and nullable dates; ToRecord builds the immutable record.
    private sealed class Row
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid HouseholdId { get; set; }
        public string Name { get; set; } = "";
        public byte[] TokenHash { get; set; } = [];
        public string TokenHint { get; set; } = "";
        public string[] Scopes { get; set; } = [];
        public Guid? DefaultListId { get; set; }
        public Guid? DefaultGroupId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset? LastUsedAt { get; set; }
        public DateTimeOffset? RevokedAt { get; set; }

        public AccessTokenRecord ToRecord() => new(
            Id, UserId, HouseholdId, Name, TokenHash, TokenHint, Scopes, DefaultListId, DefaultGroupId,
            CreatedAt, ExpiresAt, LastUsedAt, RevokedAt);
    }

    public async Task InsertAsync(AccessTokenRecord token, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO integrations.access_tokens
                (id, user_id, household_id, name, token_hash, token_hint, scopes,
                 default_list_id, default_group_id, created_at, expires_at)
            VALUES
                (@Id, @UserId, @HouseholdId, @Name, @TokenHash, @TokenHint, @Scopes,
                 @DefaultListId, @DefaultGroupId, @CreatedAt, @ExpiresAt)
            """,
            token);
    }

    public async Task<AccessTokenRecord?> FindByHashAsync(byte[] tokenHash, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<Row>(
            $"SELECT {Columns} FROM integrations.access_tokens WHERE token_hash = @tokenHash",
            new { tokenHash });
        return row?.ToRecord();
    }

    public async Task<IReadOnlyList<AccessTokenRecord>> ListActiveAsync(Guid userId, Guid householdId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Row>(
            $"""
            SELECT {Columns} FROM integrations.access_tokens
            WHERE user_id = @userId AND household_id = @householdId AND revoked_at IS NULL
            ORDER BY created_at DESC
            """,
            new { userId, householdId });
        return rows.Select(r => r.ToRecord()).ToList();
    }

    public async Task<bool> RevokeAsync(Guid id, Guid userId, Guid householdId, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.ExecuteAsync(
            """
            UPDATE integrations.access_tokens SET revoked_at = @now
            WHERE id = @id AND user_id = @userId AND household_id = @householdId AND revoked_at IS NULL
            """,
            new { id, userId, householdId, now });
        return rows == 1;
    }

    public async Task RevokeAllAsync(Guid userId, Guid householdId, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE integrations.access_tokens SET revoked_at = @now
            WHERE user_id = @userId AND household_id = @householdId AND revoked_at IS NULL
            """,
            new { userId, householdId, now });
    }

    // Written at most once an hour so ordinary requests stay read-only.
    public async Task TouchAsync(Guid id, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE integrations.access_tokens SET last_used_at = @now
            WHERE id = @id AND (last_used_at IS NULL OR last_used_at <= @threshold)
            """,
            new { id, now, threshold = now.AddHours(-1) });
    }
}
