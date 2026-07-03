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
        return Expense.Record(HouseholdId, GroupId, "Groceries", Now, funding, allocations, Now, UserId1);
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
    public async Task Record_sets_CorrectedFromExpenseId_when_supplied()
    {
        var originalId = Guid.NewGuid();
        var expense = Expense.Record(
            HouseholdId, GroupId, "Groceries", Now,
            [new FundingSource(UserId1, 1000)], [new Allocation(UserId1, 500), new Allocation(UserId2, 500)],
            Now, UserId1, correctedFromExpenseId: originalId);

        var raised = (ExpenseRecorded)expense.DomainEvents[0];
        await Assert.That(raised.CorrectedFromExpenseId).IsEqualTo(originalId);
    }

    [Test]
    public async Task Record_defaults_CorrectedFromExpenseId_to_null()
    {
        var expense = ValidExpense();
        var raised = (ExpenseRecorded)expense.DomainEvents[0];

        await Assert.That(raised.CorrectedFromExpenseId).IsNull();
    }

    [Test]
    public async Task Record_throws_when_funding_exceeds_allocations()
    {
        var funding = new[] { new FundingSource(UserId1, 1000) };
        var allocations = new[] { new Allocation(UserId1, 800) };

        await Assert.That(() =>
            Expense.Record(HouseholdId, GroupId, "X", Now, funding, allocations, Now, UserId1))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Record_throws_when_amount_is_zero()
    {
        var funding = new[] { new FundingSource(UserId1, 0) };
        var allocations = new[] { new Allocation(UserId1, 0) };

        await Assert.That(() =>
            Expense.Record(HouseholdId, GroupId, "X", Now, funding, allocations, Now, UserId1))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Record_throws_when_amount_is_negative()
    {
        var funding = new[] { new FundingSource(UserId1, -100) };
        var allocations = new[] { new Allocation(UserId1, -100) };

        await Assert.That(() =>
            Expense.Record(HouseholdId, GroupId, "X", Now, funding, allocations, Now, UserId1))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Void_marks_expense_voided_and_raises_event()
    {
        var expense = ValidExpense();
        expense.ClearEvents();

        expense.Void("mistake", Now, UserId1);

        await Assert.That(expense.IsVoided).IsTrue();
        await Assert.That(expense.DomainEvents.Count).IsEqualTo(1);
        await Assert.That(expense.DomainEvents[0] is ExpenseVoided).IsTrue();
    }

    [Test]
    public async Task Void_twice_throws()
    {
        var expense = ValidExpense();
        expense.Void(null, Now, UserId1);

        await Assert.That(() => expense.Void(null, Now, UserId1)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Record_sets_PerformedByUserId()
    {
        var expense = Expense.Record(
            HouseholdId, GroupId, "Groceries", Now,
            [new FundingSource(UserId1, 1000)], [new Allocation(UserId1, 1000)],
            Now, UserId1);

        var raised = (ExpenseRecorded)expense.DomainEvents[0];
        await Assert.That(raised.PerformedByUserId).IsEqualTo(UserId1);
    }

    [Test]
    public async Task Record_sets_RecurringExpenseId_when_supplied()
    {
        var recurringId = Guid.NewGuid();
        var expense = Expense.Record(
            HouseholdId, GroupId, "Groceries", Now,
            [new FundingSource(UserId1, 1000)], [new Allocation(UserId1, 1000)],
            Now, UserId1, recurringExpenseId: recurringId);

        var raised = (ExpenseRecorded)expense.DomainEvents[0];
        await Assert.That(raised.RecurringExpenseId).IsEqualTo(recurringId);
    }

    [Test]
    public async Task Record_defaults_RecurringExpenseId_to_null()
    {
        var expense = ValidExpense();
        var raised = (ExpenseRecorded)expense.DomainEvents[0];

        await Assert.That(raised.RecurringExpenseId).IsNull();
    }

    [Test]
    public async Task Record_uses_supplied_id_when_given()
    {
        var preassignedId = Guid.NewGuid();
        var expense = Expense.Record(
            HouseholdId, GroupId, "Groceries", Now,
            [new FundingSource(UserId1, 1000)], [new Allocation(UserId1, 1000)],
            Now, UserId1, id: preassignedId);

        await Assert.That(expense.Id).IsEqualTo(preassignedId);
    }

    [Test]
    public async Task Void_sets_PerformedByUserId_and_Description_and_CorrectedByExpenseId()
    {
        var expense = Expense.Record(
            HouseholdId, GroupId, "Groceries", Now,
            [new FundingSource(UserId1, 1000)], [new Allocation(UserId1, 1000)],
            Now, UserId1);
        expense.ClearEvents();
        var replacementId = Guid.NewGuid();

        expense.Void("Edited", Now, UserId2, correctedByExpenseId: replacementId);

        var raised = (ExpenseVoided)expense.DomainEvents[0];
        await Assert.That(raised.PerformedByUserId).IsEqualTo(UserId2);
        await Assert.That(raised.Description).IsEqualTo("Groceries");
        await Assert.That(raised.CorrectedByExpenseId).IsEqualTo(replacementId);
    }
}
