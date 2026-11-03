using System.Text.Json;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
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

        var groups = await session.QueryAsync(new AllExpenseGroupsForHousehold { HouseholdId = householdId }, ct);
        var deleted = groups.Where(g => g.DeletedAt is not null).Select(g => g.Id).ToArray();

        var query = session.Query<ActivityEntry>().Where(a => a.HouseholdId == householdId);
        if (decoded is not null)
            query = query.Where(a => a.OccurredAt < decoded.OccurredAt);
        // A deleted group's entries hide with it; its deletion entry stays so it can be restored.
        if (deleted.Length > 0)
            query = query.Where(a => a.Kind == ActivityKind.GroupDeleted || a.GroupId == null || !a.GroupId.Value.IsOneOf(deleted));

        var page = (await query.OrderByDescending(a => a.OccurredAt).Take(pageSize + 1).ToListAsync(ct)).ToList();

        var hasMore = page.Count > pageSize;
        var entries = page.Take(pageSize).ToList();

        var profileIds = entries.Select(e => e.ActorUserId)
            .Concat(entries.Where(e => e.SettlementPayerId.HasValue).Select(e => e.SettlementPayerId!.Value))
            .Concat(entries.Where(e => e.SettlementRecipientId.HasValue).Select(e => e.SettlementRecipientId!.Value))
            .Distinct();
        var profiles = (await userQuery.GetByIdsAsync(profileIds, ct)).ToDictionary(p => p.Id);

        var groupMap = groups.ToDictionary(g => g.Id);
        var items = entries.Select(e => Render(e, viewerId, profiles, groupMap)).ToList();
        var nextCursor = hasMore ? Encode(new Cursor(entries[^1].OccurredAt)) : null;

        return new ActivityFeedPage(items, nextCursor);
    }

    // Group names and icons come from the groups as they are now, so every row shows the current name.
    private static ActivityFeedItem Render(
        ActivityEntry e, Guid viewerId, Dictionary<Guid, UserProfile> profiles,
        Dictionary<Guid, ExpenseGroupDocument> groups)
    {
        var group = e.GroupId is { } gid ? groups.GetValueOrDefault(gid) : null;
        // Rows stored before GroupIds existed carry names only, so they render as unlinked chips.
        var settledGroups = e.GroupIds.Count == 0
            ? e.GroupNames.Select(n => new ActivityGroupRef(null, n, GroupIcons.Default)).ToList()
            : e.GroupIds.Select((id, i) => groups.GetValueOrDefault(id) is { } g
            ? new ActivityGroupRef(g.DeletedAt is null ? g.Id : null, g.Name, GroupIcons.Display(g.Icon))
            : new ActivityGroupRef(null, e.GroupNames.ElementAtOrDefault(i) ?? "?", GroupIcons.Display(null))).ToList();
        var actor = profiles.GetValueOrDefault(e.ActorUserId)?.DisplayName ?? "?";
        var payer = e.SettlementPayerId is { } p ? profiles.GetValueOrDefault(p)?.DisplayName ?? "?" : null;
        var recipient = e.SettlementRecipientId is { } r ? profiles.GetValueOrDefault(r)?.DisplayName ?? "?" : null;
        var viewerDelta = e.ViewerDeltaCents.TryGetValue(viewerId, out var cents) ? cents : (long?)null;

        return new ActivityFeedItem(
            e.Id, e.OccurredAt, e.Kind, actor, e.Description, group?.Name ?? e.GroupName, payer, recipient, viewerDelta,
            e.GroupId, settledGroups.Select(g => g.Name).ToList(), e.SettlementCents,
            CanRestore: e.Kind == ActivityKind.GroupDeleted && group?.DeletedAt is not null,
            RenamedFrom: e.RenamedFrom,
            RenamedTo: e.RenamedTo,
            InstallmentPlanId: e.InstallmentPlanId,
            InstallmentNumber: e.InstallmentNumber,
            InstallmentCount: e.InstallmentCount,
            AmountCents: e.AmountCents,
            ReferenceCents: e.ReferenceCents,
            VoidedCount: e.VoidedCount,
            DownPaymentCents: e.DownPaymentCents,
            RecurringExpenseId: e.RecurringExpenseId,
            Frequency: e.Frequency,
            ExpenseId: e.ExpenseId,
            GroupIcon: group is null ? null : GroupIcons.Display(group.Icon),
            SettledGroups: settledGroups);
    }

    public async Task<IReadOnlyList<ActivityFeedItem>> GetPlanHistoryAsync(
        Guid householdId, Guid planId, Guid viewerId, CancellationToken ct = default)
    {
        var entries = await session.Query<ActivityEntry>()
            .Where(a => a.HouseholdId == householdId && a.InstallmentPlanId == planId)
            .OrderByDescending(a => a.OccurredAt)
            .ToListAsync(ct);
        return await RenderHistoryAsync(householdId, entries, viewerId, ct);
    }

    public async Task<IReadOnlyList<ActivityFeedItem>> GetRecurringHistoryAsync(
        Guid householdId, Guid recurringExpenseId, Guid viewerId, CancellationToken ct = default)
    {
        var entries = await session.Query<ActivityEntry>()
            .Where(a => a.HouseholdId == householdId && a.RecurringExpenseId == recurringExpenseId)
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .ToListAsync(ct);
        return await RenderHistoryAsync(householdId, entries, viewerId, ct);
    }

    private async Task<IReadOnlyList<ActivityFeedItem>> RenderHistoryAsync(
        Guid householdId, IReadOnlyList<ActivityEntry> entries, Guid viewerId, CancellationToken ct)
    {
        var profiles = (await userQuery.GetByIdsAsync(entries.Select(e => e.ActorUserId).Distinct(), ct))
            .ToDictionary(p => p.Id);
        var groups = (await session.QueryAsync(new AllExpenseGroupsForHousehold { HouseholdId = householdId }, ct))
            .ToDictionary(g => g.Id);
        return entries.Select(e => Render(e, viewerId, profiles, groups)).ToList();
    }

    private static Cursor? Decode(string? cursor) =>
        cursor is null ? null : JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(cursor));

    private static string Encode(Cursor cursor) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor));
}
