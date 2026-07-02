using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.IntegrationTests.Infrastructure;
using Marten;
using Microsoft.Extensions.Time.Testing;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class ExpenseCommandServiceVoidTests(PostgresFixture db)
{
    private IDocumentStore Store => ExpenseDocumentStore.For(db.ConnectionString);

    private static ExpenseCommandService BuildSut(IDocumentSession session) => new(
        session,
        new ThrowingRecurringExpenseRepository(),
        new ThrowingRecurringJobScheduler(),
        new ThrowingUnitOfWork(),
        new FakeTimeProvider());

    [Test]
    public async Task VoidExpenseAsync_reverses_the_ledger_balance()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var payerId = Guid.NewGuid();
        var debtorId = Guid.NewGuid();

        await using var session = Store.LightweightSession();
        var sut = BuildSut(session);

        var expenseId = await sut.RecordExpenseAsync(
            householdId, groupId, "Groceries", DateTimeOffset.UtcNow,
            [new FundingSourceDto(payerId, 1000)],
            [new AllocationDto(payerId, 500), new AllocationDto(debtorId, 500)]);

        await sut.VoidExpenseAsync(expenseId, "recorded by mistake");

        await using var qs = Store.QuerySession();
        var ledger = await qs.LoadAsync<HouseholdLedger>(householdId);
        var netCents = ledger?.Pairs.Sum(p => Math.Abs(p.Cents)) ?? 0;

        await Assert.That(netCents).IsEqualTo(0L);
    }

    [Test]
    public async Task VoidExpenseAsync_marks_the_read_model_voided()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var payerId = Guid.NewGuid();
        var debtorId = Guid.NewGuid();

        await using var session = Store.LightweightSession();
        var sut = BuildSut(session);

        var expenseId = await sut.RecordExpenseAsync(
            householdId, groupId, "Groceries", DateTimeOffset.UtcNow,
            [new FundingSourceDto(payerId, 1000)],
            [new AllocationDto(payerId, 500), new AllocationDto(debtorId, 500)]);

        await sut.VoidExpenseAsync(expenseId, "recorded by mistake");

        await using var qs = Store.QuerySession();
        var readModel = await qs.LoadAsync<ExpenseReadModel>(expenseId);

        await Assert.That(readModel!.IsVoided).IsTrue();
        await Assert.That(readModel.VoidReason).IsEqualTo("recorded by mistake");
    }
}
