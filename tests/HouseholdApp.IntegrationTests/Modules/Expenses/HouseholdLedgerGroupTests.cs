using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.IntegrationTests.Infrastructure;
using Marten;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class HouseholdLedgerGroupTests(PostgresFixture db)
{
    private IDocumentStore Store => ExpenseDocumentStore.For(db.ConnectionString);

    [Test]
    public async Task Legacy_void_without_group_reverses_the_expense_in_its_group()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var payer = Guid.NewGuid();
        var debtor = Guid.NewGuid();
        var expenseId = Guid.NewGuid();
        IReadOnlyList<FundingSource> funding = [new FundingSource(payer, 1000)];
        IReadOnlyList<Allocation> allocations = [new Allocation(payer, 500), new Allocation(debtor, 500)];

        await using (var s = Store.LightweightSession())
        {
            s.Events.Append(expenseId, new ExpenseRecorded(
                Guid.NewGuid(), DateTimeOffset.UtcNow, expenseId, householdId, groupId,
                "Dinner", DateTimeOffset.UtcNow, funding, allocations, payer));
            await s.SaveChangesAsync();
        }

        await using (var s = Store.LightweightSession())
        {
            // Shape of a void appended before ExpenseVoided carried its group.
            s.Events.Append(expenseId, new ExpenseVoided(
                Guid.NewGuid(), DateTimeOffset.UtcNow, expenseId, householdId, null,
                funding, allocations, payer, "Dinner"));
            await s.SaveChangesAsync();
        }

        await using var qs = Store.QuerySession();
        var ledger = await qs.LoadAsync<HouseholdLedger>(householdId);
        var pair = ledger!.Pairs.Single();
        await Assert.That(pair.GroupId).IsEqualTo(groupId);
        await Assert.That(pair.Cents).IsEqualTo(0L);
    }
}
