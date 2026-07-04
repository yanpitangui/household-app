using System.Text.Json;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.CompiledQueries;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Modules.Identity.Application.Ports;
using Marten;

namespace HouseholdApp.Application.Modules.Expenses.Application.Operations;

public sealed class ActivityFeedQueryService(IQuerySession session, IUserQuery userQuery) : IActivityFeedQueries
{
    private sealed record Cursor(DateTimeOffset OccurredAt);

    public async Task<ActivityFeedPage> GetActivityFeedAsync(
        Guid householdId, Guid viewerId, string? cursor, int pageSize, CancellationToken ct = default)
    {
        var decoded = Decode(cursor);

        var page = (decoded is null
            ? await session.QueryAsync(new ActivityFeedFirstPage { HouseholdId = householdId, PageSizePlusOne = pageSize + 1 }, ct)
            : await session.QueryAsync(new ActivityFeedCursorPage { HouseholdId = householdId, Before = decoded.OccurredAt, PageSizePlusOne = pageSize + 1 }, ct))
            .ToList();

        var hasMore = page.Count > pageSize;
        var entries = page.Take(pageSize).ToList();

        var profileIds = entries.Select(e => e.ActorUserId)
            .Concat(entries.Where(e => e.SettlementPayerId.HasValue).Select(e => e.SettlementPayerId!.Value))
            .Concat(entries.Where(e => e.SettlementRecipientId.HasValue).Select(e => e.SettlementRecipientId!.Value))
            .Distinct();
        var profiles = (await userQuery.GetByIdsAsync(profileIds, ct)).ToDictionary(p => p.Id);

        var items = entries.Select(e => Render(e, viewerId, profiles)).ToList();
        var nextCursor = hasMore ? Encode(new Cursor(entries[^1].OccurredAt)) : null;

        return new ActivityFeedPage(items, nextCursor);
    }

    private static ActivityFeedItem Render(
        ActivityEntry e, Guid viewerId, Dictionary<Guid, UserProfile> profiles)
    {
        var actor = profiles.GetValueOrDefault(e.ActorUserId)?.DisplayName ?? "?";
        var payer = e.SettlementPayerId is { } p ? profiles.GetValueOrDefault(p)?.DisplayName ?? "?" : null;
        var recipient = e.SettlementRecipientId is { } r ? profiles.GetValueOrDefault(r)?.DisplayName ?? "?" : null;
        var viewerDelta = e.ViewerDeltaCents.TryGetValue(viewerId, out var cents) ? cents : (long?)null;

        return new ActivityFeedItem(
            e.Id, e.OccurredAt, e.Kind, actor, e.Description, e.GroupName, payer, recipient, viewerDelta);
    }

    private static Cursor? Decode(string? cursor) =>
        cursor is null ? null : JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(cursor));

    private static string Encode(Cursor cursor) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor));
}
