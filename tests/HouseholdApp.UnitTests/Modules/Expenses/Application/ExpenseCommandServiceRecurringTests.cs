using Microsoft.Extensions.Time.Testing;
using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.Application.Shared.Scheduler;
using Marten;
using NSubstitute;

namespace HouseholdApp.UnitTests.Modules.Expenses.Application;

public sealed class ExpenseCommandServiceRecurringTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly Guid GroupId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly DateTimeOffset BaseTime = new(2026, 6, 16, 9, 30, 0, TimeSpan.Zero); // Tuesday

    private readonly IDocumentSession _session = Substitute.For<IDocumentSession>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly IRecurringExpenseRepository _recurringRepo = Substitute.For<IRecurringExpenseRepository>();
    private readonly IRecurringJobScheduler _scheduler = Substitute.For<IRecurringJobScheduler>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _time = new(BaseTime);
    private readonly ExpenseCommandService _sut;

    private static readonly IReadOnlyList<FundingSourceDto> DefaultFunding = [new FundingSourceDto(UserId, 100_00)];
    private static readonly IReadOnlyList<AllocationDto> DefaultAllocs = [new AllocationDto(UserId, 100_00)];

    public ExpenseCommandServiceRecurringTests()
    {
        _scheduler.ScheduleCronAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        _sut = new ExpenseCommandService(_session, _eventBus, _recurringRepo, _scheduler, _uow, _time);
    }

    [Test]
    public async Task CreateRecurringExpenseAsync_returns_non_empty_id()
    {
        var id = await _sut.CreateRecurringExpenseAsync(
            HouseholdId, GroupId, "Monthly rent", RecurrenceFrequency.Monthly, BaseTime.AddDays(1), DefaultFunding, DefaultAllocs);

        await Assert.That(id).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task CreateRecurringExpenseAsync_schedules_cron_derived_from_frequency_and_start()
    {
        var startAt = new DateTimeOffset(2026, 6, 16, 9, 30, 0, TimeSpan.Zero); // Tuesday
        var expectedCron = $"30 9 * * {(int)startAt.DayOfWeek}"; // Weekly → day-of-week (2)

        await _sut.CreateRecurringExpenseAsync(
            HouseholdId, GroupId, "Weekly shop", RecurrenceFrequency.Weekly, startAt, DefaultFunding, DefaultAllocs);

        await _scheduler.Received(1).ScheduleCronAsync(
            Arg.Any<string>(), expectedCron, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateRecurringExpenseAsync_monthly_cron_uses_day_of_month()
    {
        var startAt = new DateTimeOffset(2026, 6, 16, 14, 0, 0, TimeSpan.Zero);
        var expectedCron = "0 14 16 * *";

        await _sut.CreateRecurringExpenseAsync(
            HouseholdId, GroupId, "Rent", RecurrenceFrequency.Monthly, startAt, DefaultFunding, DefaultAllocs);

        await _scheduler.Received(1).ScheduleCronAsync(
            Arg.Any<string>(), expectedCron, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateRecurringExpenseAsync_annually_cron_uses_day_and_month()
    {
        var startAt = new DateTimeOffset(2026, 6, 16, 8, 0, 0, TimeSpan.Zero);
        var expectedCron = "0 8 16 6 *";

        await _sut.CreateRecurringExpenseAsync(
            HouseholdId, GroupId, "Annual fee", RecurrenceFrequency.Annually, startAt, DefaultFunding, DefaultAllocs);

        await _scheduler.Received(1).ScheduleCronAsync(
            Arg.Any<string>(), expectedCron, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateRecurringExpenseAsync_saves_recurring_expense_with_scheduler_job_id()
    {
        var jobId = Guid.NewGuid();
        _scheduler.ScheduleCronAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(jobId);

        RecurringExpense? saved = null;
        _recurringRepo.When(r => r.SaveAsync(Arg.Any<RecurringExpense>(), Arg.Any<CancellationToken>()))
            .Do(ci => saved = ci.Arg<RecurringExpense>());

        await _sut.CreateRecurringExpenseAsync(
            HouseholdId, GroupId, "Monthly rent", RecurrenceFrequency.Monthly, BaseTime.AddDays(15), DefaultFunding, DefaultAllocs);

        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.SchedulerJobId).IsEqualTo(jobId);
    }

    [Test]
    public async Task CreateRecurringExpenseAsync_unschedules_job_when_db_save_fails()
    {
        var jobId = Guid.NewGuid();
        _scheduler.ScheduleCronAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(jobId);
        _uow.CommitAsync(Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(new InvalidOperationException("DB error")));

        await Assert.That(async () => await _sut.CreateRecurringExpenseAsync(
                HouseholdId, GroupId, "Monthly rent", RecurrenceFrequency.Monthly, BaseTime.AddDays(15), DefaultFunding, DefaultAllocs))
            .Throws<InvalidOperationException>();

        await _scheduler.Received(1).UnscheduleCronAsync(jobId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateRecurringExpenseAsync_unschedules_old_job_and_schedules_new()
    {
        var oldJobId = Guid.NewGuid();
        var newJobId = Guid.NewGuid();
        var recurring = RecurringExpense.Rehydrate(
            Guid.NewGuid(), HouseholdId, GroupId, "Rent",
            RecurrenceFrequency.Monthly, BaseTime,
            true, oldJobId,
            [new FundingSource(UserId, 100_00)], [new Allocation(UserId, 100_00)]);
        _recurringRepo.GetAsync(recurring.Id, Arg.Any<CancellationToken>()).Returns(recurring);
        _scheduler.ScheduleCronAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(newJobId);

        await _sut.UpdateRecurringExpenseAsync(
            recurring.Id, "Rent updated", RecurrenceFrequency.Annually, BaseTime.AddYears(1),
            DefaultFunding, DefaultAllocs);

        await _scheduler.Received(1).UnscheduleCronAsync(oldJobId, Arg.Any<CancellationToken>());
        await _scheduler.Received(1).ScheduleCronAsync(
            Arg.Any<string>(), Arg.Any<string>(), recurring.Id, Arg.Any<CancellationToken>());
        await Assert.That(recurring.SchedulerJobId).IsEqualTo(newJobId);
    }

    [Test]
    public async Task UpdateRecurringExpenseAsync_unschedules_new_job_when_db_fails()
    {
        var oldJobId = Guid.NewGuid();
        var newJobId = Guid.NewGuid();
        var recurring = RecurringExpense.Rehydrate(
            Guid.NewGuid(), HouseholdId, GroupId, "Rent",
            RecurrenceFrequency.Monthly, BaseTime,
            true, oldJobId,
            [new FundingSource(UserId, 100_00)], [new Allocation(UserId, 100_00)]);
        _recurringRepo.GetAsync(recurring.Id, Arg.Any<CancellationToken>()).Returns(recurring);
        _scheduler.ScheduleCronAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(newJobId);
        _uow.CommitAsync(Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(new InvalidOperationException("DB error")));

        await Assert.That(async () => await _sut.UpdateRecurringExpenseAsync(
                recurring.Id, "Rent", RecurrenceFrequency.Annually, BaseTime,
                DefaultFunding, DefaultAllocs))
            .Throws<InvalidOperationException>();

        await _scheduler.Received(1).UnscheduleCronAsync(newJobId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeactivateRecurringExpenseAsync_throws_when_not_found()
    {
        _recurringRepo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RecurringExpense?)null);

        await Assert.That(async () => await _sut.DeactivateRecurringExpenseAsync(Guid.NewGuid()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DeactivateRecurringExpenseAsync_marks_expense_inactive()
    {
        var recurring = RecurringExpense.Create(
            HouseholdId, GroupId, "Rent", DefaultFunding.Select(f => new FundingSource(f.UserId, f.Cents)).ToList(),
            DefaultAllocs.Select(a => new Allocation(a.UserId, a.Cents)).ToList(),
            RecurrenceFrequency.Monthly, BaseTime);
        _recurringRepo.GetAsync(recurring.Id, Arg.Any<CancellationToken>()).Returns(recurring);

        await _sut.DeactivateRecurringExpenseAsync(recurring.Id);

        await Assert.That(recurring.IsActive).IsFalse();
    }

    [Test]
    public async Task DeactivateRecurringExpenseAsync_unschedules_when_scheduler_job_exists()
    {
        var jobId = Guid.NewGuid();
        var recurring = RecurringExpense.Rehydrate(
            Guid.NewGuid(), HouseholdId, GroupId, "Rent",
            RecurrenceFrequency.Monthly, BaseTime,
            true, jobId,
            [new FundingSource(UserId, 100_00)], [new Allocation(UserId, 100_00)]);
        _recurringRepo.GetAsync(recurring.Id, Arg.Any<CancellationToken>()).Returns(recurring);

        await _sut.DeactivateRecurringExpenseAsync(recurring.Id);

        await _scheduler.Received(1).UnscheduleCronAsync(jobId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeactivateRecurringExpenseAsync_skips_unschedule_when_no_scheduler_job()
    {
        var recurring = RecurringExpense.Rehydrate(
            Guid.NewGuid(), HouseholdId, GroupId, "Rent",
            RecurrenceFrequency.Monthly, BaseTime,
            true, null,
            [new FundingSource(UserId, 100_00)], [new Allocation(UserId, 100_00)]);
        _recurringRepo.GetAsync(recurring.Id, Arg.Any<CancellationToken>()).Returns(recurring);

        await _sut.DeactivateRecurringExpenseAsync(recurring.Id);

        await _scheduler.DidNotReceive().UnscheduleCronAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SpawnRecurringExpenseAsync_does_nothing_when_not_found()
    {
        _recurringRepo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RecurringExpense?)null);

        await _sut.SpawnRecurringExpenseAsync(Guid.NewGuid());

        await _session.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SpawnRecurringExpenseAsync_does_nothing_when_inactive()
    {
        var recurring = RecurringExpense.Rehydrate(
            Guid.NewGuid(), HouseholdId, GroupId, "Rent",
            RecurrenceFrequency.Monthly, BaseTime,
            false, null,
            [new FundingSource(UserId, 100_00)], [new Allocation(UserId, 100_00)]);
        _recurringRepo.GetAsync(recurring.Id, Arg.Any<CancellationToken>()).Returns(recurring);

        await _sut.SpawnRecurringExpenseAsync(recurring.Id);

        await _session.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SpawnRecurringExpenseAsync_appends_events_and_publishes_for_active_recurring()
    {
        var recurring = RecurringExpense.Rehydrate(
            Guid.NewGuid(), HouseholdId, GroupId, "Rent",
            RecurrenceFrequency.Monthly, BaseTime,
            true, null,
            [new FundingSource(UserId, 100_00)], [new Allocation(UserId, 100_00)]);
        _recurringRepo.GetAsync(recurring.Id, Arg.Any<CancellationToken>()).Returns(recurring);

        await _sut.SpawnRecurringExpenseAsync(recurring.Id);

        _session.Events.Received(1).Append(Arg.Any<Guid>(), Arg.Any<object[]>());
        await _session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _eventBus.Received(1).Enqueue(Arg.Any<IDomainEvent>());
    }
}
