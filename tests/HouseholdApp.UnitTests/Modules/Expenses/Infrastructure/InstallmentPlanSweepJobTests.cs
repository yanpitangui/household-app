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
    public async Task Run_sweeps_all_plans()
    {
        var commands = Substitute.For<IInstallmentPlanCommands>();

        await new InstallmentPlanSweepJob(commands).RunAsync(CancellationToken.None);

        await commands.Received(1).RunSweepAsync(Arg.Any<CancellationToken>());
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
