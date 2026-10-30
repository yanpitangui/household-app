using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Shared.Scheduler;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.Jobs;

/// <summary>Every 5 minutes: creates the Expenses of the recurring templates that fell due.</summary>
public sealed class RecurringExpenseSweepJob(IRecurringExpenseCommands commands) : IScheduledJob
{
    public const string Name = "RecurringExpenseSweep";
    public const string Cron = "*/5 * * * *";

    public Task RunAsync(CancellationToken ct) => commands.RunSweepAsync(ct);
}
