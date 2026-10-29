using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Jobs;
using HouseholdApp.Application.Shared.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace HouseholdApp.UnitTests.Modules.Expenses.Infrastructure;

public sealed class InstallmentPlanSweepJobTests
{
    [Test]
    public async Task Run_sweeps_for_the_utc_date_of_the_clock()
    {
        var commands = Substitute.For<IInstallmentPlanCommands>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 3, 0, 0, TimeSpan.Zero));

        await new InstallmentPlanSweepJob(commands, clock).RunAsync(CancellationToken.None);

        await commands.Received(1).RunSweepAsync(new DateOnly(2026, 11, 10), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AddScheduledJob_registers_the_job_under_its_name()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IInstallmentPlanCommands>());
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddScheduledJob<InstallmentPlanSweepJob>(InstallmentPlanSweepJob.Name, InstallmentPlanSweepJob.Cron);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredKeyedService<IScheduledJob>(InstallmentPlanSweepJob.Name);

        await Assert.That(job).IsTypeOf<InstallmentPlanSweepJob>();
    }
}
