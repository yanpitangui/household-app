using System.Text.Json;
using Dapper;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Shared.Persistence;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure;

internal sealed class RecurringExpenseRepository(IUnitOfWork uow) : IRecurringExpenseRepository
{
    public async Task<RecurringExpense?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<RecurringExpenseRow>(
            """
            SELECT id, household_id, expense_group_id, description,
                   recurrence_frequency, start_at, is_active,
                   funding_sources, allocations
            FROM expenses.recurring_expenses WHERE id = @id
            """,
            new { id }, uow.CurrentTransaction);

        return row is null ? null : row.ToDomain();
    }

    public async Task SaveAsync(RecurringExpense expense, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO expenses.recurring_expenses
                (id, household_id, expense_group_id, description,
                 recurrence_frequency, start_at, cron_expression,
                 is_active, funding_sources, allocations)
            VALUES
                (@Id, @HouseholdId, @ExpenseGroupId, @Description,
                 @RecurrenceFrequency, @StartAt, @CronExpression,
                 @IsActive, @FundingSources::jsonb, @Allocations::jsonb)
            ON CONFLICT (id) DO UPDATE
                SET description          = EXCLUDED.description,
                    recurrence_frequency = EXCLUDED.recurrence_frequency,
                    start_at             = EXCLUDED.start_at,
                    cron_expression      = EXCLUDED.cron_expression,
                    is_active            = EXCLUDED.is_active,
                    funding_sources      = EXCLUDED.funding_sources,
                    allocations          = EXCLUDED.allocations
            """,
            new
            {
                expense.Id,
                expense.HouseholdId,
                expense.ExpenseGroupId,
                expense.Description,
                RecurrenceFrequency = expense.Frequency.ToString(),
                expense.StartAt,
                expense.CronExpression,
                expense.IsActive,
                FundingSources = JsonSerializer.Serialize(expense.DefaultFundingSources),
                Allocations    = JsonSerializer.Serialize(expense.DefaultAllocations)
            },
            uow.CurrentTransaction);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetActiveSchedulesAsync(CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        var rows = await conn.QueryAsync<(Guid Id, string CronExpression)>(
            "SELECT id, cron_expression FROM expenses.recurring_expenses WHERE is_active",
            transaction: uow.CurrentTransaction);
        return rows.ToDictionary(r => r.Id, r => r.CronExpression);
    }

    private sealed record RecurringExpenseRow(
        Guid Id, Guid HouseholdId, Guid ExpenseGroupId,
        string Description, string RecurrenceFrequency, DateTimeOffset StartAt,
        bool IsActive, string FundingSources, string Allocations)
    {
        public RecurringExpense ToDomain()
        {
            var frequency = Enum.Parse<Domain.RecurrenceFrequency>(RecurrenceFrequency);
            var sources   = JsonSerializer.Deserialize<List<FundingSource>>(FundingSources) ?? [];
            var allocs    = JsonSerializer.Deserialize<List<Allocation>>(Allocations) ?? [];
            return RecurringExpense.Rehydrate(Id, HouseholdId, ExpenseGroupId, Description,
                frequency, StartAt, IsActive, sources, allocs);
        }
    }
}
