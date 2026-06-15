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

    private readonly IDocumentSession _session = Substitute.For<IDocumentSession>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly IRecurringExpenseRepository _recurringRepo = Substitute.For<IRecurringExpenseRepository>();
    private readonly IRecurringJobScheduler _scheduler = Substitute.For<IRecurringJobScheduler>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ExpenseCommandService _sut;

    private static readonly IReadOnlyList<FundingSourceDto> DefaultFunding = [new FundingSourceDto(UserId, 100_00)];
    private static readonly IReadOnlyList<AllocationDto> DefaultAllocs = [new AllocationDto(UserId, 100_00)];

    public ExpenseCommandServiceRecurringTests()
    {
        _scheduler.ScheduleCronAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        _sut = new ExpenseCommandService(_session, _eventBus, _recurringRepo, _scheduler, _uow, new FakeTimeProvider());
    }

    [Test]
    public async Task CreateRecurringExpenseAsync_returns_non_empty_id()
    {
        var id = await _sut.CreateRecurringExpenseAsync(
            HouseholdId, GroupId, "Monthly rent", "0 0 1 * *", DefaultFunding, DefaultAllocs);

        await Assert.That(id).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task CreateRecurringExpenseAsync_schedules_cron_with_correct_expression()
    {
        const string cron = "0 9 * * 1";

        await _sut.CreateRecurringExpenseAsync(
            HouseholdId, GroupId, "Weekly shop", cron, DefaultFunding, DefaultAllocs);

        await _scheduler.Received(1).ScheduleCronAsync(
            Arg.Any<string>(), cron, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
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
            HouseholdId, GroupId, "Monthly rent", "0 0 1 * *", DefaultFunding, DefaultAllocs);

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
                HouseholdId, GroupId, "Monthly rent", "0 0 1 * *", DefaultFunding, DefaultAllocs))
            .Throws<InvalidOperationException>();

        await _scheduler.Received(1).UnscheduleCronAsync(jobId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateRecurringExpenseAsync_throws_on_invalid_cron()
    {
        await Assert.That(async () => await _sut.CreateRecurringExpenseAsync(
                HouseholdId, GroupId, "X", "not-a-cron", DefaultFunding, DefaultAllocs))
            .Throws<Exception>();
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
            "0 0 1 * *", null);
        _recurringRepo.GetAsync(recurring.Id, Arg.Any<CancellationToken>()).Returns(recurring);

        await _sut.DeactivateRecurringExpenseAsync(recurring.Id);

        await Assert.That(recurring.IsActive).IsFalse();
    }

    [Test]
    public async Task DeactivateRecurringExpenseAsync_unschedules_when_scheduler_job_exists()
    {
        var jobId = Guid.NewGuid();
        var recurring = RecurringExpense.Rehydrate(
            Guid.NewGuid(), HouseholdId, GroupId, "Rent", "0 0 1 * *", true, jobId,
            [new FundingSource(UserId, 100_00)], [new Allocation(UserId, 100_00)]);
        _recurringRepo.GetAsync(recurring.Id, Arg.Any<CancellationToken>()).Returns(recurring);

        await _sut.DeactivateRecurringExpenseAsync(recurring.Id);

        await _scheduler.Received(1).UnscheduleCronAsync(jobId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeactivateRecurringExpenseAsync_skips_unschedule_when_no_scheduler_job()
    {
        var recurring = RecurringExpense.Rehydrate(
            Guid.NewGuid(), HouseholdId, GroupId, "Rent", "0 0 1 * *", true, null,
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
            Guid.NewGuid(), HouseholdId, GroupId, "Rent", "0 0 1 * *", false, null,
            [new FundingSource(UserId, 100_00)], [new Allocation(UserId, 100_00)]);
        _recurringRepo.GetAsync(recurring.Id, Arg.Any<CancellationToken>()).Returns(recurring);

        await _sut.SpawnRecurringExpenseAsync(recurring.Id);

        await _session.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SpawnRecurringExpenseAsync_appends_events_and_publishes_for_active_recurring()
    {
        var recurring = RecurringExpense.Rehydrate(
            Guid.NewGuid(), HouseholdId, GroupId, "Rent", "0 0 1 * *", true, null,
            [new FundingSource(UserId, 100_00)], [new Allocation(UserId, 100_00)]);
        _recurringRepo.GetAsync(recurring.Id, Arg.Any<CancellationToken>()).Returns(recurring);

        await _sut.SpawnRecurringExpenseAsync(recurring.Id);

        _session.Events.Received(1).Append(Arg.Any<Guid>(), Arg.Any<object[]>());
        await _session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _eventBus.Received(1).PublishAsync(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>());
    }
}
