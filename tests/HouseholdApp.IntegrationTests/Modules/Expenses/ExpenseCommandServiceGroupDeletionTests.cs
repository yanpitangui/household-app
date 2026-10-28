using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.IntegrationTests.Infrastructure;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
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
        new FakeTimeProvider(), NullLogger<ExpenseCommandService>.Instance);

    private async Task<(Guid householdId, Guid groupId)> SeedGroupAsync(string name = "Trip")
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        await using var s = Store.LightweightSession();
        s.Store(new ExpenseGroupDocument { Id = groupId, HouseholdId = householdId, Name = name });
        await s.SaveChangesAsync();
        return (householdId, groupId);
    }

    [Test]
    public async Task Delete_soft_deletes_a_group_with_active_expenses_and_records_an_event()
    {
        var (householdId, groupId) = await SeedGroupAsync();
        var actorId = Guid.NewGuid();
        await using (var s = Store.LightweightSession())
        {
            await BuildSut(s, actorId).RecordExpenseAsync(householdId, groupId, "Hotel", DateTimeOffset.UtcNow,
                [new FundingSourceDto(actorId, 1000)], [new AllocationDto(actorId, 1000)]);
        }

        await using (var s = Store.LightweightSession())
            await BuildSut(s, actorId).DeleteExpenseGroupAsync(householdId, groupId);

        await using var qs = Store.QuerySession();
        var doc = await qs.LoadAsync<ExpenseGroupDocument>(groupId);
        await Assert.That(doc!.DeletedAt).IsNotNull();
        var events = await qs.Events.FetchStreamAsync(groupId);
        var deleted = events.Select(e => e.Data).OfType<ExpenseGroupDeleted>().Single();
        await Assert.That(deleted.GroupName).IsEqualTo("Trip");
        await Assert.That(deleted.PerformedByUserId).IsEqualTo(actorId);
    }

    [Test]
    public async Task Restore_clears_DeletedAt_and_records_an_event()
    {
        var (householdId, groupId) = await SeedGroupAsync();
        var actorId = Guid.NewGuid();
        await using (var s = Store.LightweightSession())
            await BuildSut(s, actorId).DeleteExpenseGroupAsync(householdId, groupId);
        await using (var s = Store.LightweightSession())
            await BuildSut(s, actorId).RestoreExpenseGroupAsync(householdId, groupId);

        await using var qs = Store.QuerySession();
        await Assert.That((await qs.LoadAsync<ExpenseGroupDocument>(groupId))!.DeletedAt).IsNull();
        var events = (await qs.Events.FetchStreamAsync(groupId)).Select(e => e.Data).ToList();
        await Assert.That(events.OfType<ExpenseGroupRestored>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Delete_and_restore_are_idempotent()
    {
        var (householdId, groupId) = await SeedGroupAsync();
        var actorId = Guid.NewGuid();
        await using (var s = Store.LightweightSession())
        {
            var sut = BuildSut(s, actorId);
            await sut.DeleteExpenseGroupAsync(householdId, groupId);
            await sut.DeleteExpenseGroupAsync(householdId, groupId);
            await sut.RestoreExpenseGroupAsync(householdId, groupId);
            await sut.RestoreExpenseGroupAsync(householdId, groupId);
        }

        await using var qs = Store.QuerySession();
        var events = (await qs.Events.FetchStreamAsync(groupId)).Select(e => e.Data).ToList();
        await Assert.That(events.OfType<ExpenseGroupDeleted>().Count()).IsEqualTo(1);
        await Assert.That(events.OfType<ExpenseGroupRestored>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Delete_rejects_a_group_from_another_household()
    {
        var (_, groupId) = await SeedGroupAsync();
        await using var s = Store.LightweightSession();

        await Assert.That(async () => await BuildSut(s, Guid.NewGuid()).DeleteExpenseGroupAsync(Guid.NewGuid(), groupId))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Recording_into_a_deleted_group_is_rejected()
    {
        var (householdId, groupId) = await SeedGroupAsync();
        var actorId = Guid.NewGuid();
        await using (var s = Store.LightweightSession())
            await BuildSut(s, actorId).DeleteExpenseGroupAsync(householdId, groupId);

        await using var session = Store.LightweightSession();
        await Assert.That(async () => await BuildSut(session, actorId).RecordExpenseAsync(
                householdId, groupId, "Hotel", DateTimeOffset.UtcNow,
                [new FundingSourceDto(actorId, 1000)], [new AllocationDto(actorId, 1000)]))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Voiding_an_expense_in_a_deleted_group_is_rejected()
    {
        var (householdId, groupId) = await SeedGroupAsync();
        var actorId = Guid.NewGuid();
        Guid expenseId;
        await using (var s = Store.LightweightSession())
        {
            var sut = BuildSut(s, actorId);
            expenseId = await sut.RecordExpenseAsync(householdId, groupId, "Hotel", DateTimeOffset.UtcNow,
                [new FundingSourceDto(actorId, 1000)], [new AllocationDto(actorId, 1000)]);
            await sut.DeleteExpenseGroupAsync(householdId, groupId);
        }

        await using var session = Store.LightweightSession();
        await Assert.That(async () => await BuildSut(session, actorId).VoidExpenseAsync(householdId, expenseId, null))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Recording_into_a_group_of_another_household_is_rejected()
    {
        var (_, groupId) = await SeedGroupAsync();
        var actorId = Guid.NewGuid();

        await using var session = Store.LightweightSession();
        await Assert.That(async () => await BuildSut(session, actorId).RecordExpenseAsync(
                Guid.NewGuid(), groupId, "Hotel", DateTimeOffset.UtcNow,
                [new FundingSourceDto(actorId, 1000)], [new AllocationDto(actorId, 1000)]))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Recording_into_an_unknown_group_is_rejected()
    {
        var actorId = Guid.NewGuid();

        await using var session = Store.LightweightSession();
        await Assert.That(async () => await BuildSut(session, actorId).RecordExpenseAsync(
                Guid.NewGuid(), Guid.NewGuid(), "Hotel", DateTimeOffset.UtcNow,
                [new FundingSourceDto(actorId, 1000)], [new AllocationDto(actorId, 1000)]))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Voiding_or_editing_an_expense_of_another_household_is_rejected()
    {
        var (householdId, groupId) = await SeedGroupAsync();
        var actorId = Guid.NewGuid();
        Guid expenseId;
        await using (var s = Store.LightweightSession())
            expenseId = await BuildSut(s, actorId).RecordExpenseAsync(householdId, groupId, "Hotel", DateTimeOffset.UtcNow,
                [new FundingSourceDto(actorId, 1000)], [new AllocationDto(actorId, 1000)]);

        var otherHousehold = Guid.NewGuid();
        await using var session = Store.LightweightSession();
        var sut = BuildSut(session, actorId);
        await Assert.That(async () => await sut.VoidExpenseAsync(otherHousehold, expenseId, null))
            .Throws<InvalidOperationException>();
        await Assert.That(async () => await sut.EditExpenseAsync(
                otherHousehold, expenseId, "X", DateTimeOffset.UtcNow,
                [new FundingSourceDto(actorId, 500)], [new AllocationDto(actorId, 500)]))
            .Throws<InvalidOperationException>();

        await using var qs = Store.QuerySession();
        await Assert.That((await qs.LoadAsync<ExpenseReadModel>(expenseId))!.IsVoided).IsFalse();
    }
}
