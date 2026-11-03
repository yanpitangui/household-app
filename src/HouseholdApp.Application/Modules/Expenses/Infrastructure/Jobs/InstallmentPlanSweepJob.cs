using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Shared.Scheduler;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.Jobs;

/// <summary>Daily at 03:00 UTC (00:00 in Brasília): creates the installments that fell due on each plan's household today.</summary>
public sealed class InstallmentPlanSweepJob(IInstallmentPlanCommands commands) : IScheduledJob
{
    public const string Name = "InstallmentPlanSweep";
    public const string Cron = "0 3 * * *";

    public Task RunAsync(CancellationToken ct) =>
        commands.RunSweepAsync(ct);
}
