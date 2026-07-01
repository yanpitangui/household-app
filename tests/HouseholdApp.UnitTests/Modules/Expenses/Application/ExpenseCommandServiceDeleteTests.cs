using Microsoft.Extensions.Time.Testing;
using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.Application.Shared.Scheduler;
using Marten;
using NSubstitute;

namespace HouseholdApp.UnitTests.Modules.Expenses.Application;

public sealed class ExpenseCommandServiceDeleteTests
{
    private readonly IDocumentSession _session = Substitute.For<IDocumentSession>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly IRecurringExpenseRepository _recurringRepo = Substitute.For<IRecurringExpenseRepository>();
    private readonly IRecurringJobScheduler _scheduler = Substitute.For<IRecurringJobScheduler>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ExpenseCommandService _sut;

    public ExpenseCommandServiceDeleteTests()
    {
        _sut = new ExpenseCommandService(_session, _eventBus, _recurringRepo, _scheduler, _uow, new FakeTimeProvider());
    }

    [Test]
    public async Task DeleteExpenseGroupAsync_throws_when_group_not_found()
    {
        _session.LoadAsync<ExpenseGroupDocument>(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ExpenseGroupDocument?)null);

        await Assert.That(async () => await _sut.DeleteExpenseGroupAsync(Guid.NewGuid()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CreateExpenseGroupAsync_returns_non_empty_id()
    {
        var id = await _sut.CreateExpenseGroupAsync(Guid.NewGuid(), "Groceries", null);

        await Assert.That(id).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task CreateExpenseGroupAsync_stores_document()
    {
        ExpenseGroupDocument? storedDoc = null;
        _session.When(s => s.Store(Arg.Any<ExpenseGroupDocument[]>()))
            .Do(ci => storedDoc = ci.Arg<ExpenseGroupDocument[]>().FirstOrDefault());

        var householdId = Guid.NewGuid();
        await _sut.CreateExpenseGroupAsync(householdId, "Utilities", "Monthly bills");

        await Assert.That(storedDoc).IsNotNull();
        await Assert.That(storedDoc!.HouseholdId).IsEqualTo(householdId);
        await Assert.That(storedDoc.Name).IsEqualTo("Utilities");
    }
}
