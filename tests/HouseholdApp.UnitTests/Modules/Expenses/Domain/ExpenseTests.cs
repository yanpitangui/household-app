using HouseholdApp.Application.Modules.Expenses.Domain;

namespace HouseholdApp.UnitTests.Modules.Expenses.Domain;

public sealed class ExpenseTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly Guid GroupId = Guid.NewGuid();
    private static readonly Guid UserId1 = Guid.NewGuid();
    private static readonly Guid UserId2 = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Expense ValidExpense(
        IReadOnlyList<FundingSource>? funding = null,
        IReadOnlyList<Allocation>? allocations = null)
    {
        funding ??= [new FundingSource(UserId1, 1000)];
        allocations ??= [new Allocation(UserId1, 500), new Allocation(UserId2, 500)];
        return Expense.Record(HouseholdId, GroupId, "Groceries", Now, funding, allocations, Now);
    }

    [Test]
    public async Task Record_sets_properties()
    {
        var expense = ValidExpense();

        await Assert.That(expense.HouseholdId).IsEqualTo(HouseholdId);
        await Assert.That(expense.ExpenseGroupId).IsEqualTo(GroupId);
        await Assert.That(expense.Description).IsEqualTo("Groceries");
        await Assert.That(expense.IsVoided).IsFalse();
    }

    [Test]
    public async Task Record_raises_ExpenseRecorded_event()
    {
        var expense = ValidExpense();

        await Assert.That(expense.DomainEvents.Count).IsEqualTo(1);
        await Assert.That(expense.DomainEvents[0] is ExpenseRecorded).IsTrue();
    }

    [Test]
    public async Task Record_throws_when_funding_exceeds_allocations()
    {
        var funding = new[] { new FundingSource(UserId1, 1000) };
        var allocations = new[] { new Allocation(UserId1, 800) };

        await Assert.That(() =>
            Expense.Record(HouseholdId, GroupId, "X", Now, funding, allocations, Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Record_throws_when_amount_is_zero()
    {
        var funding = new[] { new FundingSource(UserId1, 0) };
        var allocations = new[] { new Allocation(UserId1, 0) };

        await Assert.That(() =>
            Expense.Record(HouseholdId, GroupId, "X", Now, funding, allocations, Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Record_throws_when_amount_is_negative()
    {
        var funding = new[] { new FundingSource(UserId1, -100) };
        var allocations = new[] { new Allocation(UserId1, -100) };

        await Assert.That(() =>
            Expense.Record(HouseholdId, GroupId, "X", Now, funding, allocations, Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Void_marks_expense_voided_and_raises_event()
    {
        var expense = ValidExpense();
        expense.ClearEvents();

        expense.Void("mistake", Now);

        await Assert.That(expense.IsVoided).IsTrue();
        await Assert.That(expense.DomainEvents.Count).IsEqualTo(1);
        await Assert.That(expense.DomainEvents[0] is ExpenseVoided).IsTrue();
    }

    [Test]
    public async Task Void_twice_throws()
    {
        var expense = ValidExpense();
        expense.Void(null, Now);

        await Assert.That(() => expense.Void(null, Now)).Throws<InvalidOperationException>();
    }
}
