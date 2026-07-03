using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.Application.Shared.Scheduler;
using HouseholdApp.IntegrationTests.Infrastructure;
using Marten;
using Microsoft.Extensions.Time.Testing;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class ExpenseCommandServiceEditTests(PostgresFixture db)
{
    private IDocumentStore Store => ExpenseDocumentStore.For(db.ConnectionString);

    private static ExpenseCommandService BuildSut(IDocumentSession session) => new(
        session,
        new FakeCurrentUser(Guid.NewGuid()),
        new ThrowingRecurringExpenseRepository(),
        new ThrowingRecurringJobScheduler(),
        new ThrowingUnitOfWork(),
        new FakeTimeProvider());

    [Test]
    public async Task EditExpenseAsync_reverses_old_and_applies_only_new_ledger_amounts()
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

        await sut.EditExpenseAsync(
            expenseId, "Groceries (corrected)", DateTimeOffset.UtcNow,
            [new FundingSourceDto(payerId, 1200)],
            [new AllocationDto(payerId, 600), new AllocationDto(debtorId, 600)]);

        await using var qs = Store.QuerySession();
        var ledger = await qs.LoadAsync<HouseholdLedger>(householdId);

        await Assert.That(ledger!.Pairs.Count).IsEqualTo(1);
        var pair = ledger.Pairs[0];
        var debtorOwesCents = pair.UserId1 == payerId ? pair.Cents : -pair.Cents;
        await Assert.That(debtorOwesCents).IsEqualTo(600L);
    }

    [Test]
    public async Task EditExpenseAsync_voids_old_read_model_and_creates_correlated_replacement()
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

        var newExpenseDate = DateTimeOffset.UtcNow;
        var newId = await sut.EditExpenseAsync(
            expenseId, "Groceries (corrected)", newExpenseDate,
            [new FundingSourceDto(payerId, 1200)],
            [new AllocationDto(payerId, 600), new AllocationDto(debtorId, 600)]);

        await using var qs = Store.QuerySession();
        var old = await qs.LoadAsync<ExpenseReadModel>(expenseId);
        var replacement = await qs.LoadAsync<ExpenseReadModel>(newId);

        await Assert.That(old!.IsVoided).IsTrue();
        await Assert.That(old.VoidReason).IsEqualTo("Edited");
        await Assert.That(replacement!.Description).IsEqualTo("Groceries (corrected)");
        await Assert.That(replacement.TotalCents).IsEqualTo(1200L);
        await Assert.That(replacement.CorrectedFromExpenseId).IsEqualTo(expenseId);
    }

    [Test]
    public async Task EditExpenseAsync_throws_when_expense_already_voided()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var session = Store.LightweightSession();
        var sut = BuildSut(session);

        var expenseId = await sut.RecordExpenseAsync(
            householdId, groupId, "Groceries", DateTimeOffset.UtcNow,
            [new FundingSourceDto(userId, 500)], [new AllocationDto(userId, 500)]);
        await sut.VoidExpenseAsync(expenseId, null);

        await Assert.That(async () => await sut.EditExpenseAsync(
                expenseId, "X", DateTimeOffset.UtcNow,
                [new FundingSourceDto(userId, 500)], [new AllocationDto(userId, 500)]))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task EditExpenseAsync_throws_when_expense_not_found()
    {
        await using var session = Store.LightweightSession();
        var sut = BuildSut(session);
        var userId = Guid.NewGuid();

        await Assert.That(async () => await sut.EditExpenseAsync(
                Guid.NewGuid(), "X", DateTimeOffset.UtcNow,
                [new FundingSourceDto(userId, 500)], [new AllocationDto(userId, 500)]))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task EditExpenseAsync_changing_participants_zeroes_old_pair_and_creates_new_pair()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var payerId = Guid.NewGuid();
        var debtorId = Guid.NewGuid();
        var newDebtorId = Guid.NewGuid();

        await using var session = Store.LightweightSession();
        var sut = BuildSut(session);

        var expenseId = await sut.RecordExpenseAsync(
            householdId, groupId, "Groceries", DateTimeOffset.UtcNow,
            [new FundingSourceDto(payerId, 1000)],
            [new AllocationDto(payerId, 500), new AllocationDto(debtorId, 500)]);

        await sut.EditExpenseAsync(
            expenseId, "Groceries (corrected)", DateTimeOffset.UtcNow,
            [new FundingSourceDto(payerId, 1200)],
            [new AllocationDto(payerId, 600), new AllocationDto(newDebtorId, 600)]);

        await using var qs = Store.QuerySession();
        var ledger = await qs.LoadAsync<HouseholdLedger>(householdId);

        var oldPair = ledger!.Pairs.FirstOrDefault(p =>
            (p.UserId1 == payerId && p.UserId2 == debtorId) ||
            (p.UserId1 == debtorId && p.UserId2 == payerId));
        await Assert.That(oldPair?.Cents ?? 0L).IsEqualTo(0L);

        var newPair = ledger.Pairs.FirstOrDefault(p =>
            (p.UserId1 == payerId && p.UserId2 == newDebtorId) ||
            (p.UserId1 == newDebtorId && p.UserId2 == payerId));
        await Assert.That(newPair).IsNotNull();
        var newDebtorOwesCents = newPair!.UserId1 == payerId ? newPair.Cents : -newPair.Cents;
        await Assert.That(newDebtorOwesCents).IsEqualTo(600L);
    }
}
