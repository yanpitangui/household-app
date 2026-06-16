using Dapper;
using HouseholdApp.Application.Modules.Identity.Application.Ports;
using HouseholdApp.Application.Modules.Identity.Domain;
using Npgsql;

namespace HouseholdApp.Application.Modules.Identity.Infrastructure;

public sealed class UserRepository(NpgsqlDataSource db, TimeProvider time) : IUserQuery, IUserProvisioning
{
    public async Task<UserProfile?> GetBySubjectAsync(string subject, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<UserProfile>(
            "SELECT id, subject, email, display_name AS DisplayName, picture_url AS PictureUrl FROM identity.users WHERE subject = @subject",
            new { subject });
    }

    public async Task<UserProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<UserProfile>(
            "SELECT id, subject, email, display_name AS DisplayName, picture_url AS PictureUrl FROM identity.users WHERE id = @id",
            new { id });
    }

    public async Task<IReadOnlyList<UserProfile>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var result = await conn.QueryAsync<UserProfile>(
            "SELECT id, subject, email, display_name AS DisplayName, picture_url AS PictureUrl FROM identity.users WHERE id = ANY(@ids)",
            new { ids = ids.ToArray() });
        return result.ToList();
    }

    public async Task ProvisionAsync(string subject, string email, string displayName, string? pictureUrl, CancellationToken ct = default)
    {
        var user = User.Provision(subject, email, displayName, time.GetUtcNow());
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            WITH linked AS (
                UPDATE identity.users
                SET subject = @Subject, display_name = @DisplayName, picture_url = @PictureUrl, last_login_at = @LastLoginAt
                WHERE email = @Email
                RETURNING id
            )
            INSERT INTO identity.users (id, subject, email, display_name, picture_url, created_at, last_login_at)
            SELECT @Id, @Subject, @Email, @DisplayName, @PictureUrl, @CreatedAt, @LastLoginAt
            WHERE NOT EXISTS (SELECT 1 FROM linked)
            ON CONFLICT (subject) DO UPDATE
                SET email = EXCLUDED.email,
                    display_name = EXCLUDED.display_name,
                    picture_url = EXCLUDED.picture_url,
                    last_login_at = EXCLUDED.last_login_at
            """,
            new
            {
                user.Id,
                user.Subject,
                user.Email,
                user.DisplayName,
                PictureUrl = pictureUrl,
                user.CreatedAt,
                user.LastLoginAt
            });
    }
}
