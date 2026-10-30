using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.CompiledQueries;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Modules.Identity.Application.Ports;
using Marten;
using Marten.Linq.MatchesSql;

namespace HouseholdApp.Application.Modules.Expenses.Application.Operations;

public sealed class ExpenseQueryService(
    IQuerySession querySession,
    IUserQuery userQuery,
    TimeProvider timeProvider) : IExpenseQueries
{
    private const int RecentLimit = 30;
    private const int RecentMarkerCap = 100;

    public async Task<ExpensesHome> GetExpensesHomeAsync(Guid householdId, Guid viewerId, CancellationToken ct = default)
    {
        var (ledger, groups) = await LoadLedgerAsync(householdId, ct);
        var active = groups.Select(g => g.Id).ToHashSet();
        var pairs = ledger.Pairs.Where(p => active.Contains(p.GroupId)).ToList();

        var latest = new Dictionary<Guid, Task<ExpenseReadModel?>>();
        if (groups.Count > 0)
        {
            var batch = querySession.CreateBatchQuery();
            foreach (var g in groups)
                latest[g.Id] = batch.Query(new LatestExpenseInGroup { HouseholdId = householdId, GroupId = g.Id });
            await batch.Execute(ct);
        }

        var scope = new TimelineScope(householdId, null, active.ToList(), HomeMarker(ledger, active));
        var recent = await LoadRecentAsync(scope, ct);

        var profileMap = await LoadProfilesAsync(
            pairs.SelectMany(p => new[] { p.UserId1, p.UserId2 })
                .Concat(TimelineUserIds(recent.Expenses, recent.Settlements))
                .Distinct(), ct);
        var cards = new List<GroupCard>();
        foreach (var g in groups)
        {
            var last = await latest[g.Id];
            cards.Add(new GroupCard(
                g.Id, g.Name,
                ToViewerBalances(pairs.Where(p => p.GroupId == g.Id), viewerId, profileMap),
                last?.Description, last?.Date, GroupIcons.Display(g.Icon)));
        }

        var plans = await LoadPlanSummariesAsync(recent.Expenses, ct);
        var (expenses, settlements) = MapTimeline(
            recent.Expenses, recent.Settlements, profileMap, groups.ToDictionary(g => g.Id), plans, isHome: true);
        return new ExpensesHome(
            ToViewerBalances(pairs, viewerId, profileMap), cards, expenses, settlements,
            recent.HasOlder, recent.FirstOlderCursor);
    }

    public async Task<GroupPage?> GetGroupPageAsync(
        Guid householdId, Guid groupId, Guid viewerId, CancellationToken ct = default)
    {
        var batch = querySession.CreateBatchQuery();
        var groupTask = batch.Load<ExpenseGroupDocument>(groupId);
        var ledgerTask = batch.Load<HouseholdLedger>(householdId);
        await batch.Execute(ct);

        var group = await groupTask;
        if (group is null || group.HouseholdId != householdId || group.DeletedAt is not null) return null;

        var ledger = await ledgerTask;
        var pairs = (ledger?.Pairs ?? []).Where(p => p.GroupId == groupId).ToList();
        var marker = ledger?.GroupSettledAt.TryGetValue(groupId, out var at) == true ? at : (DateTimeOffset?)null;

        var recent = await LoadRecentAsync(new TimelineScope(householdId, groupId, [], marker), ct);

        var profileMap = await LoadProfilesAsync(
            TimelineUserIds(recent.Expenses, recent.Settlements)
                .Concat(pairs.SelectMany(p => new[] { p.UserId1, p.UserId2 }))
                .Distinct(), ct);

        // GroupName stays null: the page header already names the group.
        var plans = await LoadPlanSummariesAsync(recent.Expenses, ct);
        var (expenses, settlements) = MapTimeline(
            recent.Expenses, recent.Settlements, profileMap, null, plans, isHome: false);
        return new GroupPage(
            group.Id, group.Name, ToViewerBalances(pairs, viewerId, profileMap), expenses, settlements,
            recent.HasOlder, recent.FirstOlderCursor, GroupIcons.Display(group.Icon));
    }

    public async Task<TimelinePage> GetOlderTimelineAsync(
        Guid householdId, Guid? groupId, string? cursor, int pageSize = 30, CancellationToken ct = default)
    {
        var (ledger, groups) = await LoadLedgerAsync(householdId, ct);
        TimelineScope scope;
        if (groupId is { } g)
        {
            if (groups.All(x => x.Id != g)) return new TimelinePage([], [], null);
            scope = new TimelineScope(householdId, g, [],
                ledger.GroupSettledAt.TryGetValue(g, out var at) ? at : null);
        }
        else
        {
            var active = groups.Select(x => x.Id).ToHashSet();
            scope = new TimelineScope(householdId, null, active.ToList(), HomeMarker(ledger, active));
        }

        var start = TimelineCursor.Decode(cursor);
        // A capped Recent continues after the marker first, then moves on to the entries before it.
        var window = start.InRecent && scope.Marker is { } marker
            ? Window.FromMarker(marker)
            : Window.BeforeMarker(scope.Marker);
        var expenseModels = await FetchExpensesAsync(scope, window, start.Expense, pageSize + 1, ct);
        var settlementModels = await FetchSettlementsAsync(scope, window, start.Settlement, pageSize + 1, ct);

        var (keptE, keptS) = MergeTake(expenseModels, settlementModels, pageSize);
        var hasMore = expenseModels.Count + settlementModels.Count > keptE.Count + keptS.Count;
        string? next = null;
        if (hasMore)
        {
            next = TimelineCursor.Encode(
                keptE.Count > 0 ? TimelineCursor.PointOf(keptE[^1]) : start.Expense,
                keptS.Count > 0 ? TimelineCursor.PointOf(keptS[^1]) : start.Settlement,
                start.InRecent);
        }
        else if (start.InRecent && scope.Marker is { } settledAt && await HasEntriesBeforeAsync(scope, settledAt, ct))
        {
            next = TimelineCursor.StartOfOlder;
        }

        var profileMap = await LoadProfilesAsync(TimelineUserIds(keptE, keptS).Distinct(), ct);
        var plans = await LoadPlanSummariesAsync(keptE, ct);
        var (expenses, settlements) = MapTimeline(
            keptE, keptS, profileMap, groups.ToDictionary(x => x.Id), plans, isHome: groupId is null,
            nameGroups: groupId is null);
        return new TimelinePage(expenses, settlements, next);
    }

    // "All settled up" must not sit next to a non-zero balance, so the household marker applies only while the
    // non-deleted groups net to zero for every pair.
    private static DateTimeOffset? HomeMarker(HouseholdLedger ledger, ISet<Guid> activeGroups) =>
        ledger.Pairs.Where(p => activeGroups.Contains(p.GroupId))
            .GroupBy(p => (p.UserId1, p.UserId2))
            .Any(g => g.Sum(p => p.Cents) != 0)
            ? null
            : ledger.HouseholdSettledAt;

    private async Task<bool> HasEntriesBeforeAsync(TimelineScope scope, DateTimeOffset marker, CancellationToken ct) =>
        (await FetchExpensesAsync(scope, Window.BeforeMarker(marker), null, 1, ct)).Count > 0
        || (await FetchSettlementsAsync(scope, Window.BeforeMarker(marker), null, 1, ct)).Count > 0;

    // Home rows carry the group name as a chip; the group page's header already names the group.
    private static (List<ExpenseListItem>, List<SettlementListItem>) MapTimeline(
        IReadOnlyList<ExpenseReadModel> expenseModels,
        IReadOnlyList<SettlementReadModel> settlementModels,
        Dictionary<Guid, UserProfile> profileMap,
        Dictionary<Guid, ExpenseGroupDocument>? groups,
        Dictionary<Guid, InstallmentPlanSummary> plans,
        bool isHome,
        bool nameGroups = true)
    {
        ExpenseListParticipantDto Participant(Guid userId, long cents) => new(
            userId,
            profileMap.GetValueOrDefault(userId)?.DisplayName ?? "?",
            cents,
            profileMap.GetValueOrDefault(userId)?.PictureUrl);

        ExpenseGroupDocument? GroupOf(Guid? groupId) =>
            nameGroups && groups is not null && groupId is { } id ? groups.GetValueOrDefault(id) : null;

        var expenses = expenseModels.Select(e => new ExpenseListItem(
            e.Id, e.Description, e.Date, e.TotalCents,
            e.FundingSources.Select(f => Participant(f.UserId, f.Cents)).ToList(),
            e.Allocations.Select(a => Participant(a.UserId, a.Cents)).ToList(),
            GroupOf(e.ExpenseGroupId)?.Name,
            e.RecordedAt,
            GroupOf(e.ExpenseGroupId) is { } g ? GroupIcons.Display(g.Icon) : null,
            e.Installment,
            e.InstallmentPlanId is { } pid ? plans.GetValueOrDefault(pid) : null)).ToList();

        var settlements = settlementModels.Select(s =>
        {
            if (isHome && s.IsHouseholdLead)
            {
                var cash = s.HouseholdSettlementCents ?? s.Cents;
                return new SettlementListItem(
                    s.Id, s.Date, Participant(s.PayerId, cash), Participant(s.RecipientId, cash), cash,
                    GroupName: null, IsHouseholdSettle: true, IsOffset: false);
            }

            return new SettlementListItem(
                s.Id, s.Date, Participant(s.PayerId, s.Cents), Participant(s.RecipientId, s.Cents), s.Cents,
                GroupOf(s.ExpenseGroupId)?.Name,
                IsHouseholdSettle: s.HouseholdSettlementId is not null,
                IsOffset: s.IsOffset);
        }).ToList();

        return (expenses, settlements);
    }

    // Installment rows show the whole plan's progress, so the summaries for the plans on the page load in one pass.
    private async Task<Dictionary<Guid, InstallmentPlanSummary>> LoadPlanSummariesAsync(
        IEnumerable<ExpenseReadModel> expenses, CancellationToken ct)
    {
        var planIds = expenses.Where(e => e.InstallmentPlanId is not null)
            .Select(e => e.InstallmentPlanId!.Value).Distinct().ToList();
        if (planIds.Count == 0) return [];

        var plans = await querySession.LoadManyAsync<InstallmentPlanDocument>(ct, planIds);
        var installments = await querySession.Query<ExpenseReadModel>()
            .Where(e => e.InstallmentPlanId != null && e.InstallmentPlanId.Value.IsOneOf(planIds))
            .ToListAsync(ct);
        return plans.ToDictionary(
            p => p.Id,
            p => InstallmentPlanSummaries.Build(p, installments.Where(e => e.InstallmentPlanId == p.Id).ToList()));
    }

    private static IEnumerable<Guid> TimelineUserIds(
        IEnumerable<ExpenseReadModel> expenses, IEnumerable<SettlementReadModel> settlements) =>
        expenses.SelectMany(e => e.FundingSources.Select(f => f.UserId).Concat(e.Allocations.Select(a => a.UserId)))
            .Concat(settlements.SelectMany(s => new[] { s.PayerId, s.RecipientId }));

    private sealed record TimelineScope(
        Guid HouseholdId, Guid? GroupId, IReadOnlyList<Guid> ActiveGroupIds, DateTimeOffset? Marker);

    private sealed record RecentTimeline(
        List<ExpenseReadModel> Expenses, List<SettlementReadModel> Settlements, bool HasOlder, string? FirstOlderCursor);

    // Recent starts at the settled-up marker (capped at 100 rows), so the payment that zeroed the balances heads the
    // list; without a marker it is the latest 30. Older continues from where Recent ends.
    private async Task<RecentTimeline> LoadRecentAsync(TimelineScope scope, CancellationToken ct)
    {
        if (scope.Marker is { } marker)
        {
            var window = Window.FromMarker(marker);
            var sinceE = await FetchExpensesAsync(scope, window, null, RecentMarkerCap + 1, ct);
            var sinceS = await FetchSettlementsAsync(scope, window, null, RecentMarkerCap + 1, ct);
            var (markE, markS) = MergeTake(sinceE, sinceS, RecentMarkerCap);
            if (sinceE.Count + sinceS.Count > markE.Count + markS.Count)
            {
                var capped = TimelineCursor.Encode(
                    markE.Count > 0 ? TimelineCursor.PointOf(markE[^1]) : null,
                    markS.Count > 0 ? TimelineCursor.PointOf(markS[^1]) : null,
                    inRecent: true);
                return new RecentTimeline(markE, markS, true, capped);
            }

            return new RecentTimeline(markE, markS, await HasEntriesBeforeAsync(scope, marker, ct), null);
        }

        var fetchedE = await FetchExpensesAsync(scope, Window.All, null, RecentLimit + 1, ct);
        var fetchedS = await FetchSettlementsAsync(scope, Window.All, null, RecentLimit + 1, ct);
        var (keptE, keptS) = MergeTake(fetchedE, fetchedS, RecentLimit);
        var more = fetchedE.Count + fetchedS.Count > keptE.Count + keptS.Count;
        var cursor = more
            ? TimelineCursor.Encode(
                keptE.Count > 0 ? TimelineCursor.PointOf(keptE[^1]) : null,
                keptS.Count > 0 ? TimelineCursor.PointOf(keptS[^1]) : null)
            : null;
        return new RecentTimeline(keptE, keptS, more, cursor);
    }

    private readonly record struct Window(DateTimeOffset? RecordedAfter, DateTimeOffset? RecordedAtOrBefore)
    {
        public static Window All => new(null, null);
        // PostgreSQL keeps microseconds while .NET ticks are 100ns, so the stored and parameter values can differ
        // by up to 1µs; the boundary sits that much before the marker so the events that set it land in Recent.
        private static readonly TimeSpan Precision = TimeSpan.FromTicks(10);

        public static Window FromMarker(DateTimeOffset at) => new(at - Precision, null);
        public static Window BeforeMarker(DateTimeOffset? at) => new(null, at - Precision);
    }

    private const string Keyset =
        "((d.data ->> 'Date')::timestamptz < ? OR ((d.data ->> 'Date')::timestamptz = ? AND d.id < ?))";

    private async Task<List<ExpenseReadModel>> FetchExpensesAsync(
        TimelineScope scope, Window window, KeysetPoint? after, int? take, CancellationToken ct)
    {
        var householdId = scope.HouseholdId;
        IQueryable<ExpenseReadModel> q;
        if (scope.GroupId is { } groupId)
        {
            q = querySession.Query<ExpenseReadModel>()
                .Where(e => e.HouseholdId == householdId && e.ExpenseGroupId == groupId && !e.IsVoided);
        }
        else
        {
            var active = scope.ActiveGroupIds.ToList();
            if (active.Count == 0) return [];
            q = querySession.Query<ExpenseReadModel>()
                .Where(e => e.HouseholdId == householdId && !e.IsVoided && active.Contains(e.ExpenseGroupId));
        }

        if (window.RecordedAfter is { } rAfter) q = q.Where(e => e.RecordedAt > rAfter);
        if (window.RecordedAtOrBefore is { } rUpTo) q = q.Where(e => e.RecordedAt <= rUpTo);
        if (after is { } k) q = q.Where(e => e.MatchesSql(Keyset, k.Date.ToUniversalTime(), k.Date.ToUniversalTime(), k.Id));

        var ordered = q.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id);
        return (take is { } n ? await ordered.Take(n).ToListAsync(ct) : await ordered.ToListAsync(ct)).ToList();
    }

    private async Task<List<SettlementReadModel>> FetchSettlementsAsync(
        TimelineScope scope, Window window, KeysetPoint? after, int? take, CancellationToken ct)
    {
        var householdId = scope.HouseholdId;
        IQueryable<SettlementReadModel> q;
        if (scope.GroupId is { } groupId)
        {
            q = querySession.Query<SettlementReadModel>()
                .Where(s => s.HouseholdId == householdId && s.ExpenseGroupId == groupId);
        }
        else
        {
            // Household settles appear once, as their lead leg; the legs per group stay on the group pages.
            var active = scope.ActiveGroupIds.ToList();
            q = active.Count == 0
                ? querySession.Query<SettlementReadModel>()
                    .Where(s => s.HouseholdId == householdId && s.IsHouseholdLead)
                : querySession.Query<SettlementReadModel>()
                    .Where(s => s.HouseholdId == householdId
                        && (s.IsHouseholdLead
                            || (s.HouseholdSettlementId == null && active.Contains(s.ExpenseGroupId!.Value))));
        }

        if (window.RecordedAfter is { } rAfter) q = q.Where(s => s.RecordedAt > rAfter);
        if (window.RecordedAtOrBefore is { } rUpTo) q = q.Where(s => s.RecordedAt <= rUpTo);
        if (after is { } k) q = q.Where(s => s.MatchesSql(Keyset, k.Date.ToUniversalTime(), k.Date.ToUniversalTime(), k.Id));

        var ordered = q.OrderByDescending(s => s.Date).ThenByDescending(s => s.Id);
        return (take is { } n ? await ordered.Take(n).ToListAsync(ct) : await ordered.ToListAsync(ct)).ToList();
    }

    // Takes the first <count> rows in ExpenseTimeline order (UTC day, payments first within a day) while keeping each
    // source's own Date/Id order, so the kept rows of a source are always a prefix of its keyset order and the
    // per-source cursors stay exact.
    private static (List<ExpenseReadModel>, List<SettlementReadModel>) MergeTake(
        List<ExpenseReadModel> expenses, List<SettlementReadModel> settlements, int count)
    {
        List<ExpenseReadModel> keptE = [];
        List<SettlementReadModel> keptS = [];
        int i = 0, j = 0;
        while (keptE.Count + keptS.Count < count && (i < expenses.Count || j < settlements.Count))
        {
            var takeExpense = j >= settlements.Count
                || (i < expenses.Count && DayOf(expenses[i].Date) > DayOf(settlements[j].Date));
            if (takeExpense) keptE.Add(expenses[i++]);
            else keptS.Add(settlements[j++]);
        }

        return (keptE, keptS);
    }

    private static DateOnly DayOf(DateTimeOffset date) => DateOnly.FromDateTime(date.UtcDateTime);

    public async Task<IReadOnlyList<MemberBalance>> GetBalancesAsync(
        Guid householdId, Guid viewerId, Guid? groupId = null, CancellationToken ct = default)
    {
        var (ledger, groups) = await LoadLedgerAsync(householdId, ct);
        var active = groups.Select(g => g.Id).ToHashSet();
        var pairs = ledger.Pairs
            .Where(p => active.Contains(p.GroupId) && (groupId is null || p.GroupId == groupId))
            .ToList();
        var profileMap = await LoadProfilesAsync(pairs.SelectMany(p => new[] { p.UserId1, p.UserId2 }).Distinct(), ct);
        return ToViewerBalances(pairs, viewerId, profileMap);
    }

    public async Task<SettlementPreview> PreviewHouseholdSettlementAsync(
        Guid householdId, Guid payerId, Guid recipientId, long cents, CancellationToken ct = default)
    {
        var (ledger, groups) = await LoadLedgerAsync(householdId, ct);
        var byId = groups.ToDictionary(g => g.Id);
        var split = SettlementSplitter.Split(ledger.DebtsBetween(payerId, recipientId, byId.Keys.ToHashSet()), cents);
        return new SettlementPreview(
            split.Legs.Select(l => new SettlementPreviewLeg(
                l.GroupId, byId[l.GroupId].Name, l.Cents, l.IsOffset, GroupIcons.Display(byId[l.GroupId].Icon))).ToList(),
            split.OwedCents,
            split.Error);
    }

    private async Task<(HouseholdLedger Ledger, List<ExpenseGroupDocument> ActiveGroups)> LoadLedgerAsync(
        Guid householdId, CancellationToken ct)
    {
        var batch = querySession.CreateBatchQuery();
        var ledgerTask = batch.Load<HouseholdLedger>(householdId);
        var groupsTask = batch.Query(new ExpenseGroupsForHousehold { HouseholdId = householdId });
        await batch.Execute(ct);
        return ((await ledgerTask) ?? new HouseholdLedger { Id = householdId }, (await groupsTask).ToList());
    }

    // Each pair is read from the viewer's side (PairEntry: positive = UserId2 owes UserId1) and summed across
    // groups, so a positive balance means that member owes the viewer.
    private static List<MemberBalance> ToViewerBalances(
        IEnumerable<PairEntry> pairs, Guid viewerId, Dictionary<Guid, UserProfile> profileMap) =>
        pairs
            .Where(p => p.UserId1 == viewerId || p.UserId2 == viewerId)
            .Select(p => p.UserId1 == viewerId ? (Other: p.UserId2, Cents: p.Cents) : (Other: p.UserId1, Cents: -p.Cents))
            .GroupBy(b => b.Other)
            .Select(g => (Other: g.Key, Cents: g.Sum(b => b.Cents)))
            .Where(b => b.Cents != 0)
            .Select(b => new MemberBalance(
                b.Other,
                profileMap.TryGetValue(b.Other, out var prof) ? prof.DisplayName : "Unknown",
                b.Cents,
                prof?.PictureUrl))
            .OrderByDescending(b => Math.Abs(b.Cents))
            .ToList();

    private async Task<Dictionary<Guid, UserProfile>> LoadProfilesAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var profiles = await userQuery.GetByIdsAsync(userIds, ct);
        return profiles.ToDictionary(p => p.Id);
    }

    public async Task<InstallmentPlanPage?> GetInstallmentPlanPageAsync(
        Guid householdId, Guid planId, Guid viewerId, CancellationToken ct = default)
    {
        var plan = await querySession.LoadAsync<InstallmentPlanDocument>(planId, ct);
        if (plan is null || plan.HouseholdId != householdId) return null;
        var group = await querySession.LoadAsync<ExpenseGroupDocument>(plan.ExpenseGroupId, ct);
        if (group is null || group.DeletedAt is not null) return null;

        var installments = await querySession.Query<ExpenseReadModel>()
            .Where(e => e.InstallmentPlanId == planId).ToListAsync(ct);
        var summary = InstallmentPlanSummaries.Build(plan, installments.ToList());

        // The split shown is the one the next monthly installment will use (the last one once nothing remains);
        // the monthly share comes from installment 1 onward.
        var nextNumber = summary.Slots.FirstOrDefault(s => s.Status == InstallmentSlotStatus.Upcoming)?.Number
            ?? plan.Count + 1;
        var terms = plan.ToTerms();
        var shown = terms.Allocations(plan.Segments, Math.Clamp(nextNumber, 1, plan.Count));

        // Each member's cents over the installments still to come, ordered by user id so that the Early Payoff
        // preview breaks rounding ties the same way InstallmentSchedule.Distribute does.
        var remaining = terms.RemainingShares(plan.Segments, nextNumber)
            .Select(a => (a.UserId, a.Cents))
            .ToList();

        var profiles = await LoadProfilesAsync(
            shown.Select(a => a.UserId).Concat(remaining.Select(a => a.UserId)).Append(plan.PayerId).Distinct(), ct);
        var split = shown.Select(a => new ExpenseListParticipantDto(
            a.UserId, profiles.GetValueOrDefault(a.UserId)?.DisplayName ?? "?", a.Cents,
            profiles.GetValueOrDefault(a.UserId)?.PictureUrl)).ToList();

        return new InstallmentPlanPage(
            summary, householdId, plan.ExpenseGroupId, group.Name, GroupIcons.Display(group.Icon),
            plan.PayerId, profiles.GetValueOrDefault(plan.PayerId)?.DisplayName ?? "?", plan.TotalCents,
            plan.PurchaseDate, plan.FirstDate, split,
            shown.FirstOrDefault(a => a.UserId == viewerId)?.Cents ?? 0,
            nextNumber, plan.PayoffCents,
            remaining.Select(a => new ExpenseListParticipantDto(
                a.UserId, profiles.GetValueOrDefault(a.UserId)?.DisplayName ?? "?", a.Cents,
                profiles.GetValueOrDefault(a.UserId)?.PictureUrl)).ToList(),
            plan.DownPaymentCents);
    }

    public async Task<UpcomingSummary> GetUpcomingAsync(
        Guid householdId, Guid? groupId, Guid viewerId, CancellationToken ct = default)
    {
        var groups = (await querySession.QueryAsync(new ExpenseGroupsForHousehold { HouseholdId = householdId }, ct))
            .ToDictionary(g => g.Id);
        bool InScope(Guid g) => groups.ContainsKey(g) && (groupId is null || groupId == g);

        var plans = (await querySession.Query<InstallmentPlanDocument>()
                .Where(p => p.HouseholdId == householdId && p.Status == InstallmentPlanStatus.Active)
                .ToListAsync(ct))
            .Where(p => InScope(p.ExpenseGroupId)).ToList();
        var planIds = plans.Select(p => p.Id).ToList();
        // Only the link is read: Upcoming needs which numbers exist, not the whole Expense documents.
        var links = planIds.Count == 0
            ? []
            : await querySession.Query<ExpenseReadModel>()
                .Where(e => e.InstallmentPlanId != null && e.InstallmentPlanId.Value.IsOneOf(planIds))
                .Select(e => e.Installment!)
                .ToListAsync(ct);
        var withCreated = plans.Select(p => (p, links
                .Where(l => l is { IsPayoff: false } && l.PlanId == p.Id)
                .Select(l => l.Number).ToHashSet()))
            .ToList();

        var templates = (await LoadActiveTemplatesAsync(householdId, ct)).Where(t => InScope(t.GroupId)).ToList();
        var summary = UpcomingBuilder.Build(withCreated, templates, timeProvider.GetUtcNow(), viewerId);
        if (summary.Items.Count == 0) return UpcomingSummary.Empty;

        var profiles = await LoadProfilesAsync(summary.Items.Select(i => i.PayerId).Distinct(), ct);
        var named = summary.Items.Select(i => i with
        {
            GroupName = groups[i.GroupId].Name,
            GroupIcon = GroupIcons.Display(groups[i.GroupId].Icon),
            PayerName = profiles.GetValueOrDefault(i.PayerId)?.DisplayName
        }).ToList();
        return summary with { Items = named };
    }

    private async Task<IReadOnlyList<RecurringTemplate>> LoadActiveTemplatesAsync(Guid householdId, CancellationToken ct)
    {
        var docs = await querySession.Query<RecurringExpenseDocument>()
            .Where(t => t.HouseholdId == householdId && t.Status == RecurringExpenseStatus.Active)
            .ToListAsync(ct);
        return docs.Select(t => new RecurringTemplate(
            t.Id, t.Description, t.ExpenseGroupId, t.Schedule, t.FundingSources, t.Allocations)).ToList();
    }

    public async Task<ExpenseDetail?> GetExpenseAsync(Guid expenseId, CancellationToken ct = default)
    {
        var e = await querySession.LoadAsync<ExpenseReadModel>(expenseId, ct);
        if (e is null) return null;
        return new ExpenseDetail(
            e.Id, e.HouseholdId, e.ExpenseGroupId, e.Description, e.Date,
            e.TotalCents, e.IsVoided, e.VoidReason,
            e.FundingSources.Select(f => new FundingSourceDto(f.UserId, f.Cents)).ToList(),
            e.Allocations.Select(a => new AllocationDto(a.UserId, a.Cents)).ToList(),
            e.RecordedAt, e.Installment);
    }

    public async Task<IReadOnlyList<ExpenseGroupSummary>> ListExpenseGroupsAsync(
        Guid householdId, CancellationToken ct = default)
    {
        var groups = await querySession.QueryAsync(new ExpenseGroupsForHousehold { HouseholdId = householdId }, ct);
        return groups.Select(g => new ExpenseGroupSummary(g.Id, g.Name, g.Description, GroupIcons.Display(g.Icon))).ToList();
    }

    public async Task<IReadOnlyList<RecurringExpenseSummary>> ListRecurringExpensesAsync(
        Guid householdId, CancellationToken ct = default)
    {
        var docs = await querySession.Query<RecurringExpenseDocument>()
            .Where(t => t.HouseholdId == householdId)
            .OrderBy(t => t.Description)
            .ToListAsync(ct);
        return docs.Select(t => new RecurringExpenseSummary(
            t.Id, t.Description, t.Schedule, t.Status == RecurringExpenseStatus.Active, t.ExpenseGroupId)).ToList();
    }
}
