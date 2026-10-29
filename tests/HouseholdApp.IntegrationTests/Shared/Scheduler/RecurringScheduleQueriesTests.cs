using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure;
using HouseholdApp.Application.Modules.Tasks.Domain;
using HouseholdApp.Application.Modules.Tasks.Infrastructure;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.IntegrationTests.Infrastructure;
using NSubstitute;

namespace HouseholdApp.IntegrationTests.Shared.Scheduler;

/// <summary>The active schedules each module hands the scheduler's reconciler.</summary>
[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class RecurringScheduleQueriesTests(PostgresFixture db)
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Test]
    public async Task Recurring_expenses_report_the_cron_of_active_templates_only()
    {
        await using var uow = new DapperUnitOfWork(db.DataSource, Substitute.For<IEventBus>());
        var repo = new RecurringExpenseRepository(uow);
        var active = Template(new DateTimeOffset(2026, 6, 16, 9, 30, 0, TimeSpan.Zero));
        var inactive = Template(new DateTimeOffset(2026, 6, 17, 9, 30, 0, TimeSpan.Zero));
        inactive.Deactivate();
        await repo.SaveAsync(active);
        await repo.SaveAsync(inactive);

        var schedules = await repo.GetActiveSchedulesAsync();

        await Assert.That(schedules[active.Id]).IsEqualTo(active.CronExpression);
        await Assert.That(schedules.ContainsKey(inactive.Id)).IsFalse();
    }

    [Test]
    public async Task Recurring_tasks_report_the_cron_of_active_templates_only()
    {
        await using var uow = new DapperUnitOfWork(db.DataSource, Substitute.For<IEventBus>());
        var repo = new TaskRepository(uow);
        var active = RecurringTask.Create(Guid.NewGuid(), "Vacuum", null, null, "0 9 * * 1", null);
        var inactive = RecurringTask.Create(Guid.NewGuid(), "Dust", null, null, "0 9 * * 2", null);
        inactive.Deactivate();
        await repo.SaveRecurringTaskAsync(active);
        await repo.SaveRecurringTaskAsync(inactive);

        var schedules = await repo.GetActiveRecurringSchedulesAsync();

        await Assert.That(schedules[active.Id]).IsEqualTo("0 9 * * 1");
        await Assert.That(schedules.ContainsKey(inactive.Id)).IsFalse();
    }

    private static RecurringExpense Template(DateTimeOffset startAt) => RecurringExpense.Create(
        Guid.NewGuid(), Guid.NewGuid(), "Rent",
        [new FundingSource(UserId, 100_00)], [new Allocation(UserId, 100_00)],
        RecurrenceFrequency.Monthly, startAt);
}
