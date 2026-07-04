using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.IntegrationTests.Infrastructure;
using Marten;
using Microsoft.Extensions.Time.Testing;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class ExpenseCommandServiceGroupDeletionTests(PostgresFixture db)
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
    public async Task DeleteExpenseGroupAsync_throws_when_group_has_active_expenses()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();

        await using (var s = Store.LightweightSession())
        {
            s.Store(new ExpenseGroupDocument { Id = groupId, HouseholdId = householdId, Name = "Trip" });
            s.Events.Append(expenseId, new ExpenseRecorded(
                Guid.NewGuid(), DateTimeOffset.UtcNow, expenseId, householdId, groupId,
                "Hotel", DateTimeOffset.UtcNow,
                [new FundingSource(actorId, 1000)], [new Allocation(actorId, 1000)], actorId));
            await s.SaveChangesAsync();
        }

        await using var session = Store.LightweightSession();
        var sut = BuildSut(session, actorId);

        await Assert.That(async () => await sut.DeleteExpenseGroupAsync(groupId))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DeleteExpenseGroupAsync_succeeds_when_group_has_no_active_expenses()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await using (var s = Store.LightweightSession())
        {
            s.Store(new ExpenseGroupDocument { Id = groupId, HouseholdId = householdId, Name = "Empty Group" });
            await s.SaveChangesAsync();
        }

        await using var session = Store.LightweightSession();
        var sut = BuildSut(session, actorId);

        await sut.DeleteExpenseGroupAsync(groupId);

        await using var checkSession = Store.QuerySession();
        var deleted = await checkSession.LoadAsync<ExpenseGroupDocument>(groupId);
        await Assert.That(deleted).IsNull();
    }

    [Test]
    public async Task DeleteExpenseGroupAsync_succeeds_when_group_only_has_voided_expenses()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await using (var s = Store.LightweightSession())
        {
            s.Store(new ExpenseGroupDocument { Id = groupId, HouseholdId = householdId, Name = "Voided Trip" });
            await s.SaveChangesAsync();
        }

        await using (var recordSession = Store.LightweightSession())
        {
            var recordSut = BuildSut(recordSession, actorId);
            var expenseId = await recordSut.RecordExpenseAsync(
                householdId, groupId, "Hotel", DateTimeOffset.UtcNow,
                [new FundingSourceDto(actorId, 1000)],
                [new AllocationDto(actorId, 1000)]);
            await recordSut.VoidExpenseAsync(expenseId, "recorded by mistake");
        }

        await using var session = Store.LightweightSession();
        var sut = BuildSut(session, actorId);

        await sut.DeleteExpenseGroupAsync(groupId);

        await using var checkSession = Store.QuerySession();
        var deleted = await checkSession.LoadAsync<ExpenseGroupDocument>(groupId);
        await Assert.That(deleted).IsNull();
    }
}
