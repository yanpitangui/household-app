using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Shared.Scheduler;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.Jobs;

/// <summary>Creates the Expense of a RecurringExpense each time its cron fires.</summary>
public sealed class RecurringExpenseJobs(IExpenseCommands expenseCommands, IRecurringExpenseRepository repository) : IRecurringJob
{
    public const string Name = "RecurringExpense";

    public Task RunAsync(Guid entityId, CancellationToken ct) =>
        expenseCommands.SpawnRecurringExpenseAsync(entityId, ct);

    public Task<IReadOnlyDictionary<Guid, string>> GetActiveSchedulesAsync(CancellationToken ct) =>
        repository.GetActiveSchedulesAsync(ct);
}
