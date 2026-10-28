using Microsoft.Extensions.Time.Testing;
using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.Application.Shared.Scheduler;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HouseholdApp.UnitTests.Modules.Expenses.Application;

public sealed class ExpenseCommandServiceActorTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly Guid GroupId = Guid.NewGuid();
    private static readonly Guid CurrentUserId = Guid.NewGuid();

    private readonly IDocumentSession _session = Substitute.For<IDocumentSession>();
    private readonly IRecurringExpenseRepository _recurringRepo = Substitute.For<IRecurringExpenseRepository>();
    private readonly IRecurringJobScheduler _scheduler = Substitute.For<IRecurringJobScheduler>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ExpenseCommandService _sut;

    public ExpenseCommandServiceActorTests()
    {
        _currentUser.Id.Returns(CurrentUserId);
        _session.LoadAsync<ExpenseGroupDocument>(GroupId, Arg.Any<CancellationToken>())
            .Returns(new ExpenseGroupDocument { Id = GroupId, HouseholdId = HouseholdId, Name = "Home" });
        _sut = new ExpenseCommandService(
            _session, _currentUser, _recurringRepo, _scheduler, _uow, new FakeTimeProvider(), NullLogger<ExpenseCommandService>.Instance);
    }

    [Test]
    public async Task RecordExpenseAsync_stamps_PerformedByUserId_from_ICurrentUser()
    {
        var payerId = Guid.NewGuid();
        object[]? appended = null;
        _session.Events.When(e => e.Append(Arg.Any<Guid>(), Arg.Any<object[]>()))
            .Do(ci => appended = ci.Arg<object[]>());

        await _sut.RecordExpenseAsync(
            HouseholdId, GroupId, "Groceries", DateTimeOffset.UtcNow,
            [new FundingSourceDto(payerId, 500)], [new AllocationDto(payerId, 500)]);

        var recorded = (ExpenseRecorded)appended![0];
        await Assert.That(recorded.PerformedByUserId).IsEqualTo(CurrentUserId);
    }

    [Test]
    public async Task RecordExpenseAsync_rejects_a_group_of_another_household()
    {
        var payerId = Guid.NewGuid();

        await Assert.That(async () => await _sut.RecordExpenseAsync(
                Guid.NewGuid(), GroupId, "Groceries", DateTimeOffset.UtcNow,
                [new FundingSourceDto(payerId, 500)], [new AllocationDto(payerId, 500)]))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RecordExpenseAsync_rejects_an_unknown_group()
    {
        var payerId = Guid.NewGuid();

        await Assert.That(async () => await _sut.RecordExpenseAsync(
                HouseholdId, Guid.NewGuid(), "Groceries", DateTimeOffset.UtcNow,
                [new FundingSourceDto(payerId, 500)], [new AllocationDto(payerId, 500)]))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RecordExpenseAsync_rejects_a_deleted_group()
    {
        var payerId = Guid.NewGuid();
        _session.LoadAsync<ExpenseGroupDocument>(GroupId, Arg.Any<CancellationToken>())
            .Returns(new ExpenseGroupDocument
            {
                Id = GroupId, HouseholdId = HouseholdId, Name = "Home", DeletedAt = DateTimeOffset.UtcNow
            });

        await Assert.That(async () => await _sut.RecordExpenseAsync(
                HouseholdId, GroupId, "Groceries", DateTimeOffset.UtcNow,
                [new FundingSourceDto(payerId, 500)], [new AllocationDto(payerId, 500)]))
            .Throws<InvalidOperationException>();
    }
}
