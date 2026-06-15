using HouseholdApp.Application.Modules.Expenses.Domain;

namespace HouseholdApp.UnitTests.Modules.Expenses.Domain;

public sealed class RecurringExpenseTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly Guid GroupId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static RecurringExpense Valid(string cron = "0 9 * * *") =>
        RecurringExpense.Create(
            HouseholdId, GroupId, "Monthly rent",
            [new FundingSource(UserId, 100_00)],
            [new Allocation(UserId, 100_00)],
            cron, null);

    [Test]
    public async Task Create_throws_when_funding_not_equal_allocations()
    {
        await Assert.That(() => RecurringExpense.Create(
                HouseholdId, GroupId, "X",
                [new FundingSource(UserId, 1000)],
                [new Allocation(UserId, 500)],
                "0 9 * * *", null))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Create_is_active_by_default()
    {
        var recurring = Valid();

        await Assert.That(recurring.IsActive).IsTrue();
    }

    [Test]
    public async Task Create_stores_cron_expression()
    {
        var recurring = Valid("0 0 1 * *");

        await Assert.That(recurring.CronExpression).IsEqualTo("0 0 1 * *");
    }

    [Test]
    public async Task Create_generates_non_empty_id()
    {
        var recurring = Valid();

        await Assert.That(recurring.Id).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task Spawn_returns_expense_with_same_household_and_group()
    {
        var recurring = Valid();

        var expense = recurring.Spawn(DateTimeOffset.UtcNow);

        await Assert.That(expense.HouseholdId).IsEqualTo(HouseholdId);
        await Assert.That(expense.ExpenseGroupId).IsEqualTo(GroupId);
    }

    [Test]
    public async Task Spawn_returns_expense_with_same_amounts()
    {
        var recurring = Valid();
        var expense = recurring.Spawn(DateTimeOffset.UtcNow);
        var evt = (ExpenseRecorded)expense.DomainEvents[0];

        await Assert.That(evt.FundingSources.Sum(f => f.Cents)).IsEqualTo(100_00L);
        await Assert.That(evt.Allocations.Sum(a => a.Cents)).IsEqualTo(100_00L);
    }

    [Test]
    public async Task Spawn_raises_expense_recorded_event()
    {
        var recurring = Valid();

        var expense = recurring.Spawn(DateTimeOffset.UtcNow);

        await Assert.That(expense.DomainEvents.Count).IsEqualTo(1);
        await Assert.That(expense.DomainEvents[0] is ExpenseRecorded).IsTrue();
    }

    [Test]
    public async Task Deactivate_sets_is_active_false()
    {
        var recurring = Valid();

        recurring.Deactivate();

        await Assert.That(recurring.IsActive).IsFalse();
    }

    [Test]
    public async Task SetSchedulerJobId_stores_id()
    {
        var recurring = Valid();
        var jobId = Guid.NewGuid();

        recurring.SetSchedulerJobId(jobId);

        await Assert.That(recurring.SchedulerJobId).IsEqualTo(jobId);
    }

    [Test]
    public async Task Rehydrate_roundtrips_all_properties()
    {
        var id = Guid.NewGuid();
        var schedulerJobId = Guid.NewGuid();

        var recurring = RecurringExpense.Rehydrate(
            id, HouseholdId, GroupId, "Rent", "0 9 * * *", false, schedulerJobId,
            [new FundingSource(UserId, 5000)],
            [new Allocation(UserId, 5000)]);

        await Assert.That(recurring.Id).IsEqualTo(id);
        await Assert.That(recurring.HouseholdId).IsEqualTo(HouseholdId);
        await Assert.That(recurring.IsActive).IsFalse();
        await Assert.That(recurring.SchedulerJobId).IsEqualTo(schedulerJobId);
        await Assert.That(recurring.DefaultFundingSources[0].Cents).IsEqualTo(5000L);
    }
}
