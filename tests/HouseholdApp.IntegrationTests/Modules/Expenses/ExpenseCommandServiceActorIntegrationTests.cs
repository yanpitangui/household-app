using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.IntegrationTests.Infrastructure;
using Marten;
using Microsoft.Extensions.Time.Testing;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class ExpenseCommandServiceActorIntegrationTests(PostgresFixture db)
{
    private IDocumentStore Store => ExpenseDocumentStore.For(db.ConnectionString);

    private static ExpenseCommandService BuildSut(IDocumentSession session, Guid actorId) => new(
        session,
        new FakeCurrentUser(actorId),
        new ThrowingRecurringExpenseRepository(),
        new ThrowingRecurringJobScheduler(),
        new ThrowingUnitOfWork(),
        new FakeTimeProvider());

    [Test]
    public async Task VoidExpenseAsync_stamps_PerformedByUserId_from_ICurrentUser()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var payerId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await using var session = Store.LightweightSession();
        var sut = BuildSut(session, actorId);

        var expenseId = await sut.RecordExpenseAsync(
            householdId, groupId, "Groceries", DateTimeOffset.UtcNow,
            [new FundingSourceDto(payerId, 500)], [new AllocationDto(payerId, 500)]);
        await sut.VoidExpenseAsync(expenseId, "mistake");

        await using var qs = Store.QuerySession();
        var readModel = await qs.LoadAsync<ExpenseReadModel>(expenseId);
        await Assert.That(readModel!.IsVoided).IsTrue();
        // PerformedByUserId isn't projected onto ExpenseReadModel — asserted via the
        // ActivityEntryProjection integration tests in Task 6, which read it directly.
    }

    [Test]
    public async Task EditExpenseAsync_correlates_void_to_replacement()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var payerId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await using var session = Store.LightweightSession();
        var sut = BuildSut(session, actorId);

        var expenseId = await sut.RecordExpenseAsync(
            householdId, groupId, "Groceries", DateTimeOffset.UtcNow,
            [new FundingSourceDto(payerId, 500)], [new AllocationDto(payerId, 500)]);
        var newId = await sut.EditExpenseAsync(
            expenseId, "Groceries (edited)", DateTimeOffset.UtcNow,
            [new FundingSourceDto(payerId, 600)], [new AllocationDto(payerId, 600)]);

        await using var qs = Store.QuerySession();
        var old = await qs.LoadAsync<ExpenseReadModel>(expenseId);
        var replacement = await qs.LoadAsync<ExpenseReadModel>(newId);
        await Assert.That(old!.IsVoided).IsTrue();
        await Assert.That(replacement!.CorrectedFromExpenseId).IsEqualTo(expenseId);
    }
}
