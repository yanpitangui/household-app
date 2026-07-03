using Dapper;
using HouseholdApp.Application.Modules.Notifications.Application.Ports;
using Npgsql;

namespace HouseholdApp.Application.Modules.Notifications.Infrastructure;

public sealed class PushSubscriptionRepository(NpgsqlDataSource db) : IPushSubscriptionCommands
{
    public async Task SubscribeAsync(Guid userId, string endpoint, string p256dh, string auth, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO notifications.push_subscriptions (endpoint, user_id, p256dh, auth)
            VALUES (@endpoint, @userId, @p256dh, @auth)
            ON CONFLICT (endpoint) DO UPDATE
                SET user_id = EXCLUDED.user_id, p256dh = EXCLUDED.p256dh, auth = EXCLUDED.auth
            """,
            new { endpoint, userId, p256dh, auth });
    }

    public async Task UnsubscribeAsync(Guid userId, string endpoint, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            "DELETE FROM notifications.push_subscriptions WHERE user_id = @userId AND endpoint = @endpoint",
            new { userId, endpoint });
    }

    public async Task<IReadOnlyList<PushSubscriptionInfo>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var result = await conn.QueryAsync<PushSubscriptionInfo>(
            """
            SELECT user_id AS UserId, endpoint AS Endpoint, p256dh AS P256dh, auth AS Auth
            FROM notifications.push_subscriptions WHERE user_id = @userId
            """,
            new { userId });
        return result.ToList();
    }

    public async Task DeleteByEndpointAsync(string endpoint, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            "DELETE FROM notifications.push_subscriptions WHERE endpoint = @endpoint",
            new { endpoint });
    }
}
