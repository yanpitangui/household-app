using Dapper;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.CompiledQueries;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Modules.Identity.Application.Ports;
using Marten;
using Npgsql;

namespace HouseholdApp.Application.Modules.Expenses.Application.Operations;

public sealed class ExpenseQueryService(
    IQuerySession querySession,
    NpgsqlDataSource db,
    IUserQuery userQuery) : IExpenseQueries
{
    public async Task<ExpensesSummary> GetExpensesSummaryAsync(
        Guid householdId, Guid viewerId, Guid? groupId = null, CancellationToken ct = default)
    {
        var batch = querySession.CreateBatchQuery();
        var expensesTask = groupId.HasValue
            ? batch.Query(new ExpensesForGroup { HouseholdId = householdId, GroupId = groupId.Value })
            : batch.Query(new ExpensesForHousehold { HouseholdId = householdId });
        var settlementsTask = groupId.HasValue
            ? batch.Query(new SettlementsForGroup { HouseholdId = householdId, GroupId = groupId.Value })
            : batch.Query(new SettlementsForHousehold { HouseholdId = householdId });
        var groupsTask = batch.Query(new ExpenseGroupsForHousehold { HouseholdId = householdId });
        var ledgerTask = batch.Load<HouseholdLedger>(householdId);
        await batch.Execute(ct);

        var expenseModels = await expensesTask;
        var settlementModels = await settlementsTask;
        var groupNames = (await groupsTask).ToDictionary(g => g.Id, g => g.Name);
        var pairs = (await ledgerTask)?.Pairs ?? [];

        var allUserIds = expenseModels
            .SelectMany(e => e.FundingSources.Select(f => f.UserId)
                .Concat(e.Allocations.Select(a => a.UserId)))
            .Concat(settlementModels.SelectMany(s => new[] { s.PayerId, s.RecipientId }))
            .Concat(pairs.SelectMany(p => new[] { p.UserId1, p.UserId2 }))
            .Distinct();
        var profileMap = await LoadProfilesAsync(allUserIds, ct);

        ExpenseListParticipantDto Participant(Guid userId, long cents) => new(
            userId,
            profileMap.GetValueOrDefault(userId)?.DisplayName ?? "?",
            cents,
            profileMap.GetValueOrDefault(userId)?.PictureUrl);

        var expenses = expenseModels.Select(e => new ExpenseListItem(
            e.Id, e.Description, e.Date, e.TotalCents,
            e.FundingSources.Select(f => Participant(f.UserId, f.Cents)).ToList(),
            e.Allocations.Select(a => Participant(a.UserId, a.Cents)).ToList(),
            groupNames.GetValueOrDefault(e.ExpenseGroupId)
        )).ToList();

        var settlements = settlementModels.Select(s => new SettlementListItem(
            s.Id, s.Date,
            Participant(s.PayerId, s.Cents),
            Participant(s.RecipientId, s.Cents),
            s.Cents,
            s.ExpenseGroupId is { } gid ? groupNames.GetValueOrDefault(gid) : null
        )).ToList();

        return new ExpensesSummary(expenses, settlements, ToViewerBalances(pairs, viewerId, profileMap));
    }

    public async Task<IReadOnlyList<MemberBalance>> GetBalancesAsync(
        Guid householdId, Guid viewerId, CancellationToken ct = default)
    {
        var pairs = await LoadPairsAsync(householdId, ct);
        var profileMap = await LoadProfilesAsync(pairs.SelectMany(p => new[] { p.UserId1, p.UserId2 }).Distinct(), ct);
        return ToViewerBalances(pairs, viewerId, profileMap);
    }

    private async Task<IReadOnlyList<PairEntry>> LoadPairsAsync(Guid householdId, CancellationToken ct)
    {
        var ledger = await querySession.LoadAsync<HouseholdLedger>(householdId, ct);
        return ledger?.Pairs ?? [];
    }

    private async Task<Dictionary<Guid, UserProfile>> LoadProfilesAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var profiles = await userQuery.GetByIdsAsync(userIds, ct);
        return profiles.ToDictionary(p => p.Id);
    }

    // Each pair is read from the viewer's side (PairEntry: positive = UserId2 owes UserId1),
    // so a positive balance means that member owes the viewer.
    private static List<MemberBalance> ToViewerBalances(
        IReadOnlyList<PairEntry> pairs, Guid viewerId, Dictionary<Guid, UserProfile> profileMap) =>
        pairs
            .Where(p => p.Cents != 0 && (p.UserId1 == viewerId || p.UserId2 == viewerId))
            .Select(p => p.UserId1 == viewerId ? (Other: p.UserId2, Cents: p.Cents) : (Other: p.UserId1, Cents: -p.Cents))
            .Select(b => new MemberBalance(
                b.Other,
                profileMap.TryGetValue(b.Other, out var prof) ? prof.DisplayName : "Unknown",
                b.Cents,
                prof?.PictureUrl))
            .OrderByDescending(b => Math.Abs(b.Cents))
            .ToList();

    public async Task<ExpenseDetail?> GetExpenseAsync(Guid expenseId, CancellationToken ct = default)
    {
        var e = await querySession.LoadAsync<ExpenseReadModel>(expenseId, ct);
        if (e is null) return null;
        return new ExpenseDetail(
            e.Id, e.HouseholdId, e.ExpenseGroupId, e.Description, e.Date,
            e.TotalCents, e.IsVoided, e.VoidReason,
            e.FundingSources.Select(f => new FundingSourceDto(f.UserId, f.Cents)).ToList(),
            e.Allocations.Select(a => new AllocationDto(a.UserId, a.Cents)).ToList(),
            e.RecordedAt);
    }

    public async Task<IReadOnlyList<ExpenseGroupSummary>> ListExpenseGroupsAsync(
        Guid householdId, CancellationToken ct = default)
    {
        var groups = await querySession.QueryAsync(new ExpenseGroupsForHousehold { HouseholdId = householdId }, ct);
        return groups.Select(g => new ExpenseGroupSummary(g.Id, g.Name, g.Description)).ToList();
    }

    public async Task<IReadOnlyList<RecurringExpenseSummary>> ListRecurringExpensesAsync(
        Guid householdId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<RecurringExpenseRow>(
            """
            SELECT id, description, recurrence_frequency, start_at, is_active, expense_group_id
            FROM expenses.recurring_expenses
            WHERE household_id = @householdId
            ORDER BY description
            """,
            new { householdId });
        return rows.Select(r => r.ToSummary()).ToList();
    }

    private sealed record RecurringExpenseRow(
        Guid Id, string Description, string RecurrenceFrequency,
        DateTimeOffset StartAt, bool IsActive, Guid ExpenseGroupId)
    {
        public RecurringExpenseSummary ToSummary() =>
            new(Id, Description, Enum.Parse<RecurrenceFrequency>(RecurrenceFrequency), StartAt, IsActive, ExpenseGroupId);
    }
}
