using Dapper;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Modules.Identity.Application.Ports;
using Marten;
using Npgsql;

namespace HouseholdApp.Application.Modules.Expenses.Application.Operations;

internal sealed class ExpenseQueryService(
    IQuerySession querySession,
    NpgsqlDataSource db,
    IUserQuery userQuery) : IExpenseQueries
{
    public async Task<IReadOnlyList<ExpenseListItem>> ListExpensesAsync(
        Guid householdId, Guid? groupId = null, CancellationToken ct = default)
    {
        var query = querySession.Query<ExpenseReadModel>()
            .Where(e => e.HouseholdId == householdId && !e.IsVoided);

        if (groupId.HasValue)
            query = query.Where(e => e.ExpenseGroupId == groupId.Value);

        var results = await query.OrderByDescending(e => e.Date).ToListAsync(ct);
        return results.Select(e => new ExpenseListItem(e.Id, e.Description, e.Date, e.TotalCents, e.IsVoided))
            .ToList();
    }

    public async Task<ExpenseDetail?> GetExpenseAsync(Guid expenseId, CancellationToken ct = default)
    {
        var e = await querySession.LoadAsync<ExpenseReadModel>(expenseId, ct);
        if (e is null) return null;
        return new ExpenseDetail(
            e.Id, e.ExpenseGroupId, e.Description, e.Date,
            e.TotalCents, e.IsVoided, e.VoidReason,
            e.FundingSources.Select(f => new FundingSourceDto(f.UserId, f.Cents)).ToList(),
            e.Allocations.Select(a => new AllocationDto(a.UserId, a.Cents)).ToList(),
            e.RecordedAt);
    }

    public async Task<IReadOnlyList<MemberBalance>> GetHouseholdBalancesAsync(
        Guid householdId, CancellationToken ct = default)
    {
        var ledger = await querySession.LoadAsync<HouseholdLedger>(householdId, ct);
        var pairs = ledger?.Pairs ?? [];

        var userIds = pairs.SelectMany(p => new[] { p.UserId1, p.UserId2 }).Distinct();
        var profiles = await userQuery.GetByIdsAsync(userIds, ct);
        var profileMap = profiles.ToDictionary(p => p.Id);

        var netPerUser = new Dictionary<Guid, long>();
        foreach (var p in pairs)
        {
            netPerUser[p.UserId1] = netPerUser.GetValueOrDefault(p.UserId1) + p.Cents;
            netPerUser[p.UserId2] = netPerUser.GetValueOrDefault(p.UserId2) - p.Cents;
        }

        return netPerUser
            .Select(kv => new MemberBalance(
                kv.Key,
                profileMap.TryGetValue(kv.Key, out var prof) ? prof.DisplayName : "Unknown",
                kv.Value))
            .OrderByDescending(b => Math.Abs(b.Cents))
            .ToList();
    }

    public async Task<IReadOnlyList<ExpenseGroupSummary>> ListExpenseGroupsAsync(
        Guid householdId, CancellationToken ct = default)
    {
        var groups = await querySession.Query<ExpenseGroupDocument>()
            .Where(g => g.HouseholdId == householdId)
            .OrderBy(g => g.Name)
            .ToListAsync(ct);
        return groups.Select(g => new ExpenseGroupSummary(g.Id, g.Name, g.Description)).ToList();
    }

    public async Task<IReadOnlyList<RecurringExpenseSummary>> ListRecurringExpensesAsync(
        Guid householdId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<RecurringExpenseSummary>(
            """
            SELECT id, description, cron_expression, is_active, expense_group_id
            FROM expenses.recurring_expenses
            WHERE household_id = @householdId
            ORDER BY description
            """,
            new { householdId });
        return rows.ToList();
    }
}
