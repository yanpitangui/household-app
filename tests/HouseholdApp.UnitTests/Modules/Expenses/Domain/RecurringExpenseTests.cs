using HouseholdApp.Application.Modules.Expenses.Domain;

namespace HouseholdApp.UnitTests.Modules.Expenses.Domain;

public sealed class RecurringExpenseTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly Guid GroupId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly DateTimeOffset BaseTime = new(2026, 6, 16, 9, 30, 0, TimeSpan.Zero); // Tuesday

    private static RecurringExpense Valid(
        RecurrenceFrequency frequency = RecurrenceFrequency.Monthly,
        DateTimeOffset? startAt = null) =>
        RecurringExpense.Create(
            HouseholdId, GroupId, "Monthly rent",
            [new FundingSource(UserId, 100_00)],
            [new Allocation(UserId, 100_00)],
            frequency, startAt ?? BaseTime);

    [Test]
    public async Task Create_throws_when_funding_not_equal_allocations()
    {
        await Assert.That(() => RecurringExpense.Create(
                HouseholdId, GroupId, "X",
                [new FundingSource(UserId, 1000)],
                [new Allocation(UserId, 500)],
                RecurrenceFrequency.Monthly, BaseTime))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Create_is_active_by_default()
    {
        var recurring = Valid();

        await Assert.That(recurring.IsActive).IsTrue();
    }

    [Test]
    public async Task Create_generates_non_empty_id()
    {
        var recurring = Valid();

        await Assert.That(recurring.Id).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task Create_stores_frequency_and_start_at()
    {
        var recurring = Valid(RecurrenceFrequency.Annually, BaseTime);

        await Assert.That(recurring.Frequency).IsEqualTo(RecurrenceFrequency.Annually);
        await Assert.That(recurring.StartAt).IsEqualTo(BaseTime);
    }

    [Test]
    [Arguments(RecurrenceFrequency.Weekly,   "30 9 * * 2")] // Tuesday = 2
    [Arguments(RecurrenceFrequency.Monthly,  "30 9 16 * *")]
    [Arguments(RecurrenceFrequency.Annually, "30 9 16 6 *")]
    public async Task ComputeCron_produces_correct_pattern(RecurrenceFrequency frequency, string expectedCron)
    {
        var cron = RecurringExpense.ComputeCron(frequency, BaseTime);

        await Assert.That(cron).IsEqualTo(expectedCron);
    }

    [Test]
    public async Task Create_cron_expression_matches_computed_cron()
    {
        var recurring = Valid(RecurrenceFrequency.Monthly, BaseTime);

        await Assert.That(recurring.CronExpression).IsEqualTo(RecurringExpense.ComputeCron(RecurrenceFrequency.Monthly, BaseTime));
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
    public async Task Update_recomputes_cron_and_updates_all_fields()
    {
        var recurring = Valid(RecurrenceFrequency.Monthly, BaseTime);
        var newStart = BaseTime.AddMonths(6);

        recurring.Update(
            RecurrenceFrequency.Annually, newStart, "New desc",
            [new FundingSource(UserId, 200_00)],
            [new Allocation(UserId, 200_00)]);

        await Assert.That(recurring.Frequency).IsEqualTo(RecurrenceFrequency.Annually);
        await Assert.That(recurring.StartAt).IsEqualTo(newStart);
        await Assert.That(recurring.Description).IsEqualTo("New desc");
        await Assert.That(recurring.CronExpression).IsEqualTo(RecurringExpense.ComputeCron(RecurrenceFrequency.Annually, newStart));
        await Assert.That(recurring.DefaultFundingSources[0].Cents).IsEqualTo(200_00L);
    }

    [Test]
    public async Task Rehydrate_roundtrips_all_properties()
    {
        var id = Guid.NewGuid();
        var schedulerJobId = Guid.NewGuid();

        var recurring = RecurringExpense.Rehydrate(
            id, HouseholdId, GroupId, "Rent",
            RecurrenceFrequency.Monthly, BaseTime,
            false, schedulerJobId,
            [new FundingSource(UserId, 5000)],
            [new Allocation(UserId, 5000)]);

        await Assert.That(recurring.Id).IsEqualTo(id);
        await Assert.That(recurring.HouseholdId).IsEqualTo(HouseholdId);
        await Assert.That(recurring.Frequency).IsEqualTo(RecurrenceFrequency.Monthly);
        await Assert.That(recurring.StartAt).IsEqualTo(BaseTime);
        await Assert.That(recurring.IsActive).IsFalse();
        await Assert.That(recurring.SchedulerJobId).IsEqualTo(schedulerJobId);
        await Assert.That(recurring.DefaultFundingSources[0].Cents).IsEqualTo(5000L);
    }
}
