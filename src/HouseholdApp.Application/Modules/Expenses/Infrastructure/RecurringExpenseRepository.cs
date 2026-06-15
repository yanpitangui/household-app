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
            SELECT id, household_id, expense_group_id, description, cron_expression,
                   is_active, scheduler_job_id, funding_sources, allocations
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
                (id, household_id, expense_group_id, description, cron_expression, is_active, scheduler_job_id, funding_sources, allocations)
            VALUES
                (@Id, @HouseholdId, @ExpenseGroupId, @Description, @CronExpression, @IsActive, @SchedulerJobId, @FundingSources::jsonb, @Allocations::jsonb)
            ON CONFLICT (id) DO UPDATE
                SET is_active        = EXCLUDED.is_active,
                    scheduler_job_id = EXCLUDED.scheduler_job_id
            """,
            new
            {
                expense.Id,
                expense.HouseholdId,
                expense.ExpenseGroupId,
                expense.Description,
                expense.CronExpression,
                expense.IsActive,
                expense.SchedulerJobId,
                FundingSources = JsonSerializer.Serialize(expense.DefaultFundingSources),
                Allocations    = JsonSerializer.Serialize(expense.DefaultAllocations)
            },
            uow.CurrentTransaction);
    }

    private sealed record RecurringExpenseRow(
        Guid Id, Guid HouseholdId, Guid ExpenseGroupId,
        string Description, string CronExpression, bool IsActive,
        Guid? SchedulerJobId, string FundingSources, string Allocations)
    {
        public RecurringExpense ToDomain()
        {
            var sources = JsonSerializer.Deserialize<List<FundingSource>>(FundingSources) ?? [];
            var allocs  = JsonSerializer.Deserialize<List<Allocation>>(Allocations) ?? [];
            return RecurringExpense.Rehydrate(Id, HouseholdId, ExpenseGroupId, Description,
                CronExpression, IsActive, SchedulerJobId, sources, allocs);
        }
    }
}
