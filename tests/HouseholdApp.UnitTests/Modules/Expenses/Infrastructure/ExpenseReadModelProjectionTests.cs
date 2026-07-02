using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

namespace HouseholdApp.UnitTests.Modules.Expenses.Infrastructure;

public sealed class ExpenseReadModelProjectionTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly ExpenseReadModelProjection _projection = new();

    [Test]
    public async Task Apply_ExpenseRecorded_sets_all_fields()
    {
        var expenseId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var funding = new[] { new FundingSource(UserId, 1000) };
        var allocations = new[] { new Allocation(UserId, 1000) };
        var evt = new ExpenseRecorded(Guid.NewGuid(), Now, expenseId, HouseholdId, groupId,
            "Groceries", Now, funding, allocations);
        var model = new ExpenseReadModel();

        _projection.Apply(evt, model);

        await Assert.That(model.Id).IsEqualTo(expenseId);
        await Assert.That(model.HouseholdId).IsEqualTo(HouseholdId);
        await Assert.That(model.ExpenseGroupId).IsEqualTo(groupId);
        await Assert.That(model.Description).IsEqualTo("Groceries");
        await Assert.That(model.TotalCents).IsEqualTo(1000L);
        await Assert.That(model.IsVoided).IsFalse();
    }

    [Test]
    public async Task Apply_ExpenseVoided_marks_voided_with_reason()
    {
        var model = new ExpenseReadModel { Id = Guid.NewGuid() };

        _projection.Apply(
            new ExpenseVoided(Guid.NewGuid(), Now, model.Id, HouseholdId, "duplicate", [], []), model);

        await Assert.That(model.IsVoided).IsTrue();
        await Assert.That(model.VoidReason).IsEqualTo("duplicate");
    }
}
