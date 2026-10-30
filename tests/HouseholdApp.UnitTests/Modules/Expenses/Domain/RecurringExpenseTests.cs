using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.UnitTests.Modules.Expenses.Domain;

public sealed class RecurringExpenseTests
{
    private static readonly Guid Household = Guid.NewGuid();
    private static readonly Guid Group = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Leo = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<FundingSource> Funding = [new(Ana, 100_00)];
    private static readonly IReadOnlyList<Allocation> Split = [new(Ana, 50_00), new(Leo, 50_00)];

    private static RecurrenceSchedule Monthly(int y, int m, int d, int hour = 9) =>
        new(RecurrenceFrequency.Monthly, new DateOnly(y, m, d), new TimeOnly(hour, 0));

    private static RecurringExpense Create(RecurrenceSchedule schedule, bool catchUp = false) =>
        RecurringExpense.Create(Household, Group, "Rent", Funding, Split, schedule, catchUp, Now, Ana);

    [Test]
    public async Task Create_rejects_funding_that_differs_from_allocations()
    {
        await Assert.That(() => RecurringExpense.Create(
                Household, Group, "Rent", Funding, [new Allocation(Ana, 1)], Monthly(2026, 12, 1), false, Now, Ana))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Create_rejects_a_zero_amount()
    {
        await Assert.That(() => RecurringExpense.Create(
                Household, Group, "Rent", [new FundingSource(Ana, 0)], [new Allocation(Ana, 0)], Monthly(2026, 12, 1),
                false, Now, Ana))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Update_rejects_a_zero_amount()
    {
        var template = Create(Monthly(2026, 12, 1));

        await Assert.That(() => template.Update(
                "Rent", [new FundingSource(Ana, 0)], [new Allocation(Ana, 0)], Monthly(2026, 12, 1), Now, Ana))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Create_with_a_future_first_date_starts_at_the_first_occurrence()
    {
        var template = Create(Monthly(2026, 12, 1));

        await Assert.That(template.NextOccurrenceAt).IsEqualTo(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task Create_today_with_the_time_passed_is_due_right_away()
    {
        var template = Create(Monthly(2026, 11, 10, hour: 9));

        await Assert.That(template.DueOccurrences(Now).Count).IsEqualTo(1);
    }

    [Test]
    public async Task Create_in_the_past_without_catch_up_only_sets_the_rhythm()
    {
        var template = Create(Monthly(2026, 1, 15));

        await Assert.That(template.NextOccurrenceAt).IsEqualTo(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero));
        await Assert.That(template.DueOccurrences(Now)).IsEmpty();
    }

    [Test]
    public async Task Create_in_the_past_with_catch_up_leaves_every_past_occurrence_due()
    {
        var template = Create(Monthly(2026, 9, 1), catchUp: true);

        var due = template.DueOccurrences(Now);

        await Assert.That(due).IsEquivalentTo(new[]
        {
            new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1)
        });
    }

    [Test]
    public async Task Create_rejects_a_catch_up_over_the_cap()
    {
        var schedule = new RecurrenceSchedule(RecurrenceFrequency.Daily, new DateOnly(2026, 1, 1), new TimeOnly(9, 0));

        await Assert.That(() => Create(schedule, catchUp: true)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task MarkOccurred_advances_to_the_next_occurrence()
    {
        var template = Create(Monthly(2026, 10, 31), catchUp: true);

        template.MarkOccurred(new DateOnly(2026, 10, 31), Now);

        await Assert.That(template.NextOccurrenceAt).IsEqualTo(new DateTimeOffset(2026, 11, 30, 9, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task BuildExpense_uses_the_derived_id_and_the_occurrence_date()
    {
        var template = Create(Monthly(2026, 11, 1), catchUp: true);
        var date = new DateOnly(2026, 11, 1);

        var expense = template.BuildExpense(date, Now, coveredByCreatedEvent: false);

        await Assert.That(expense.Id).IsEqualTo(RecurrenceSchedule.OccurrenceId(template.Id, date));
        await Assert.That(expense.Date).IsEqualTo(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        var recorded = (ExpenseRecorded)expense.DomainEvents.Single();
        await Assert.That(recorded.RecurringExpenseId).IsEqualTo(template.Id);
        await Assert.That(recorded.PerformedByUserId).IsEqualTo(Ana);
    }

    [Test]
    public async Task Update_with_a_new_schedule_moves_next_to_the_first_after_now()
    {
        var template = Create(Monthly(2026, 12, 1));

        template.Update("Rent", Funding, Split, Monthly(2026, 11, 20), Now, Ana);

        await Assert.That(template.NextOccurrenceAt).IsEqualTo(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task Update_with_the_same_schedule_keeps_next_and_records_the_previous_amount()
    {
        var template = Create(Monthly(2026, 12, 1));
        template.ClearEvents();

        template.Update("Rent", [new FundingSource(Ana, 120_00)],
            [new Allocation(Ana, 60_00), new Allocation(Leo, 60_00)], Monthly(2026, 12, 1), Now, Ana);

        var updated = (RecurringExpenseUpdated)template.DomainEvents.Single();
        await Assert.That(updated.PreviousAmountCents).IsEqualTo(100_00);
        await Assert.That(template.NextOccurrenceAt).IsEqualTo(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task Update_allows_an_unchanged_past_first_date_and_rejects_a_changed_one()
    {
        var template = Create(Monthly(2026, 1, 15));

        template.Update("Renamed", Funding, Split, Monthly(2026, 1, 15), Now, Ana);

        await Assert.That(() => template.Update("Rent", Funding, Split, Monthly(2026, 2, 15), Now, Ana))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task End_is_final()
    {
        var template = Create(Monthly(2026, 12, 1));

        template.End(Now, Ana);

        await Assert.That(template.Status).IsEqualTo(RecurringExpenseStatus.Ended);
        await Assert.That(template.DueOccurrences(Now.AddYears(1))).IsEmpty();
        await Assert.That(() => template.Update("Rent", Funding, Split, Monthly(2026, 12, 1), Now, Ana))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Replaying_the_events_rebuilds_the_same_state()
    {
        var template = Create(Monthly(2026, 12, 1));
        template.Update("Renamed", Funding, Split, Monthly(2026, 12, 5), Now, Ana);
        var events = template.DomainEvents.ToList();

        var replayed = RecurringExpense.Create((RecurringExpenseCreated)events[0]);
        replayed.Apply((RecurringExpenseUpdated)events[1]);

        await Assert.That(replayed.Description).IsEqualTo("Renamed");
        await Assert.That(replayed.NextOccurrenceAt).IsEqualTo(template.NextOccurrenceAt);
    }
}
