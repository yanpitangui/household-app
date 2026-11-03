using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.UnitTests.Modules.Expenses.Domain;

public sealed class RecurringExpenseTests
{
    private static readonly Guid Household = Guid.NewGuid();
    private static readonly Guid Group = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Leo = Guid.NewGuid();
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<FundingSource> Funding = [new(Ana, 100_00)];
    private static readonly IReadOnlyList<Allocation> Split = [new(Ana, 50_00), new(Leo, 50_00)];

    private static RecurrenceSchedule Monthly(int y, int m, int d, int hour = 9) =>
        new(RecurrenceFrequency.Monthly, new DateOnly(y, m, d), new TimeOnly(hour, 0));

    private static RecurringExpense Create(RecurrenceSchedule schedule, bool catchUp = false) =>
        RecurringExpense.Create(Household, Group, "Rent", Funding, Split, schedule, Utc, catchUp, Now, Ana);

    [Test]
    public async Task Create_rejects_funding_that_differs_from_allocations()
    {
        await Assert.That(() => RecurringExpense.Create(
                Household, Group, "Rent", Funding, [new Allocation(Ana, 1)], Monthly(2026, 12, 1), Utc, false, Now, Ana))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Create_rejects_a_zero_amount()
    {
        await Assert.That(() => RecurringExpense.Create(
                Household, Group, "Rent", [new FundingSource(Ana, 0)], [new Allocation(Ana, 0)], Monthly(2026, 12, 1),
                Utc, false, Now, Ana))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Update_rejects_a_zero_amount()
    {
        var template = Create(Monthly(2026, 12, 1));

        await Assert.That(() => template.Update(
                "Rent", [new FundingSource(Ana, 0)], [new Allocation(Ana, 0)], Monthly(2026, 12, 1), Utc, Now, Ana))
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

        await Assert.That(template.DueOccurrences(Now, Utc).Count).IsEqualTo(1);
    }

    [Test]
    public async Task Create_in_the_past_without_catch_up_only_sets_the_rhythm()
    {
        var template = Create(Monthly(2026, 1, 15));

        await Assert.That(template.NextOccurrenceAt).IsEqualTo(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero));
        await Assert.That(template.DueOccurrences(Now, Utc)).IsEmpty();
    }

    [Test]
    public async Task Create_in_the_past_with_catch_up_leaves_every_past_occurrence_due()
    {
        var template = Create(Monthly(2026, 9, 1), catchUp: true);

        var due = template.DueOccurrences(Now, Utc);

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

        template.MarkOccurred(new DateOnly(2026, 10, 31), Now, Utc);

        await Assert.That(template.NextOccurrenceAt).IsEqualTo(new DateTimeOffset(2026, 11, 30, 9, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task MarkOccurred_skipped_advances_and_flags_the_event()
    {
        var template = Create(Monthly(2026, 10, 31), catchUp: true);

        template.MarkOccurred(new DateOnly(2026, 10, 31), Now, Utc, skipped: true);

        await Assert.That(template.NextOccurrenceAt).IsEqualTo(new DateTimeOffset(2026, 11, 30, 9, 0, 0, TimeSpan.Zero));
        var occurred = template.DomainEvents.OfType<RecurringExpenseOccurred>().Last();
        await Assert.That(occurred.Skipped).IsTrue();
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

        template.Update("Rent", Funding, Split, Monthly(2026, 11, 20), Utc, Now, Ana);

        await Assert.That(template.NextOccurrenceAt).IsEqualTo(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task Update_with_the_same_schedule_keeps_next_and_records_the_previous_amount()
    {
        var template = Create(Monthly(2026, 12, 1));
        template.ClearEvents();

        template.Update("Rent", [new FundingSource(Ana, 120_00)],
            [new Allocation(Ana, 60_00), new Allocation(Leo, 60_00)], Monthly(2026, 12, 1), Utc, Now, Ana);

        var updated = (RecurringExpenseUpdated)template.DomainEvents.Single();
        await Assert.That(updated.PreviousAmountCents).IsEqualTo(100_00);
        await Assert.That(template.NextOccurrenceAt).IsEqualTo(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task Update_allows_an_unchanged_past_first_date_and_rejects_a_changed_one()
    {
        var template = Create(Monthly(2026, 1, 15));

        template.Update("Renamed", Funding, Split, Monthly(2026, 1, 15), Utc, Now, Ana);

        await Assert.That(() => template.Update("Rent", Funding, Split, Monthly(2026, 2, 15), Utc, Now, Ana))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task End_is_final()
    {
        var template = Create(Monthly(2026, 12, 1));

        template.End(Now, Ana);

        await Assert.That(template.Status).IsEqualTo(RecurringExpenseStatus.Ended);
        await Assert.That(template.DueOccurrences(Now.AddYears(1), Utc)).IsEmpty();
        await Assert.That(() => template.Update("Rent", Funding, Split, Monthly(2026, 12, 1), Utc, Now, Ana))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Pause_stops_due_occurrences()
    {
        var template = Create(Monthly(2026, 12, 1));

        template.Pause(Now, Ana);

        await Assert.That(template.Status).IsEqualTo(RecurringExpenseStatus.Paused);
        await Assert.That(template.DueOccurrences(Now.AddYears(1), Utc)).IsEmpty();
        await Assert.That(template.DomainEvents.OfType<RecurringExpensePaused>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Resume_skips_paused_dates()
    {
        var created = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
        var template = RecurringExpense.Create(
            Household, Group, "Rent", Funding, Split, Monthly(2026, 1, 10), Utc, false, created, Ana);
        template.Pause(new DateTimeOffset(2026, 1, 11, 12, 0, 0, TimeSpan.Zero), Ana);

        template.Resume(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero), Utc, Ana);

        var expected = new DateTimeOffset(2026, 4, 10, 9, 0, 0, TimeSpan.Zero);
        await Assert.That(template.Status).IsEqualTo(RecurringExpenseStatus.Active);
        await Assert.That(template.NextOccurrenceAt).IsEqualTo(expected);
        var resumed = template.DomainEvents.OfType<RecurringExpenseResumed>().Single();
        await Assert.That(resumed.NextOccurrenceAt).IsEqualTo(expected);
    }

    [Test]
    public async Task Resume_before_first_date_keeps_first_date()
    {
        var template = Create(Monthly(2026, 12, 1));
        var first = template.NextOccurrenceAt;
        template.Pause(Now, Ana);

        template.Resume(Now, Utc, Ana);

        await Assert.That(template.NextOccurrenceAt).IsEqualTo(first);
    }

    [Test]
    public async Task Pause_rejects_paused_and_ended()
    {
        var paused = Create(Monthly(2026, 12, 1));
        paused.Pause(Now, Ana);
        var ended = Create(Monthly(2026, 12, 1));
        ended.End(Now, Ana);

        await Assert.That(() => paused.Pause(Now, Ana))
            .Throws<InvalidOperationException>().WithMessage("This recurring expense is not active.");
        await Assert.That(() => ended.Pause(Now, Ana))
            .Throws<InvalidOperationException>().WithMessage("This recurring expense has ended.");
    }

    [Test]
    public async Task Resume_rejects_active_and_ended()
    {
        var active = Create(Monthly(2026, 12, 1));
        var ended = Create(Monthly(2026, 12, 1));
        ended.End(Now, Ana);

        await Assert.That(() => active.Resume(Now, Utc, Ana))
            .Throws<InvalidOperationException>().WithMessage("This recurring expense is not paused.");
        await Assert.That(() => ended.Resume(Now, Utc, Ana))
            .Throws<InvalidOperationException>().WithMessage("This recurring expense has ended.");
    }

    [Test]
    public async Task Update_is_allowed_while_paused()
    {
        var template = Create(Monthly(2026, 12, 1));
        template.Pause(Now, Ana);

        template.Update("Renamed", Funding, Split, Monthly(2026, 12, 5), Utc, Now, Ana);

        await Assert.That(template.Description).IsEqualTo("Renamed");
        await Assert.That(template.Status).IsEqualTo(RecurringExpenseStatus.Paused);
    }

    [Test]
    public async Task End_is_allowed_while_paused()
    {
        var template = Create(Monthly(2026, 12, 1));
        template.Pause(Now, Ana);

        template.End(Now, Ana);

        await Assert.That(template.Status).IsEqualTo(RecurringExpenseStatus.Ended);
    }

    [Test]
    public async Task Replaying_the_events_rebuilds_the_same_state()
    {
        var template = Create(Monthly(2026, 12, 1));
        template.Update("Renamed", Funding, Split, Monthly(2026, 12, 5), Utc, Now, Ana);
        var events = template.DomainEvents.ToList();

        var replayed = RecurringExpense.Create((RecurringExpenseCreated)events[0]);
        replayed.Apply((RecurringExpenseUpdated)events[1]);

        await Assert.That(replayed.Description).IsEqualTo("Renamed");
        await Assert.That(replayed.NextOccurrenceAt).IsEqualTo(template.NextOccurrenceAt);
    }

    [Test]
    public async Task Replaying_pause_and_resume_rebuilds_the_same_state()
    {
        var template = Create(Monthly(2026, 12, 1));
        template.Pause(Now, Ana);
        template.Resume(Now.AddDays(30), Utc, Ana);
        var events = template.DomainEvents.ToList();

        var replayed = RecurringExpense.Create((RecurringExpenseCreated)events[0]);
        replayed.Apply((RecurringExpensePaused)events[1]);
        replayed.Apply((RecurringExpenseResumed)events[2]);

        await Assert.That(replayed.Status).IsEqualTo(template.Status);
        await Assert.That(replayed.NextOccurrenceAt).IsEqualTo(template.NextOccurrenceAt);
    }

    [Test]
    public async Task RemoveMember_hands_their_share_to_the_others_and_keeps_the_schedule()
    {
        var bia = Guid.NewGuid();
        var template = RecurringExpense.Create(
            Household, Group, "Rent", Funding, [new(Ana, 50_00), new(bia, 25_00), new(Leo, 25_00)],
            Monthly(2026, 12, 1), Utc, false, Now, Ana);
        var next = template.NextOccurrenceAt;

        var changed = template.RemoveMember(bia, Now, Utc, Ana);

        // Bia's 25.00 splits 50:25 between Ana and Leo: Ana 16.67 (66.67 total), Leo 8.33 (33.33 total); the leftover cent goes to Ana.
        await Assert.That(changed).IsTrue();
        await Assert.That(template.Allocations.Select(a => a.UserId)).IsEquivalentTo(new[] { Ana, Leo });
        await Assert.That(template.Allocations.Sum(a => a.Cents)).IsEqualTo(100_00);
        await Assert.That(template.NextOccurrenceAt).IsEqualTo(next);
        await Assert.That(template.DomainEvents.OfType<RecurringExpenseUpdated>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task RemoveMember_ends_a_paused_template_left_with_only_its_payer()
    {
        var template = Create(Monthly(2026, 12, 1));
        template.Pause(Now, Ana);

        template.RemoveMember(Leo, Now, Utc, Ana);

        // Leo leaving leaves only the payer, so the template ends.
        await Assert.That(template.Status).IsEqualTo(RecurringExpenseStatus.Ended);
    }

    [Test]
    public async Task RemoveMember_leaves_a_paused_template_paused_when_others_remain()
    {
        var bia = Guid.NewGuid();
        var template = RecurringExpense.Create(
            Household, Group, "Rent", Funding, [new(Ana, 40_00), new(bia, 30_00), new(Leo, 30_00)],
            Monthly(2026, 12, 1), Utc, false, Now, Ana);
        template.Pause(Now, Ana);

        template.RemoveMember(bia, Now, Utc, Ana);

        await Assert.That(template.Status).IsEqualTo(RecurringExpenseStatus.Paused);
        await Assert.That(template.Allocations.Select(a => a.UserId)).IsEquivalentTo(new[] { Ana, Leo });
    }

    [Test]
    public async Task RemoveMember_ignores_an_ended_template()
    {
        var template = Create(Monthly(2026, 12, 1));
        template.End(Now, Ana);

        await Assert.That(template.RemoveMember(Leo, Now, Utc, Ana)).IsFalse();
    }

    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    [Test]
    public async Task A_late_evening_occurrence_is_due_on_the_next_utc_day_and_dated_on_its_local_day()
    {
        // 31 Oct 22:00 in Sao Paulo is 1 Nov 01:00Z.
        var schedule = new RecurrenceSchedule(RecurrenceFrequency.Monthly, new DateOnly(2026, 10, 31), new TimeOnly(22, 0));
        var template = RecurringExpense.Create(
            Household, Group, "Rent", Funding, Split, schedule, SaoPaulo, false, new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), Ana);
        var sweep = new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero);

        await Assert.That(template.DueOccurrences(sweep.AddMinutes(-1), SaoPaulo)).IsEmpty();
        await Assert.That(template.DueOccurrences(sweep, SaoPaulo)).IsEquivalentTo(new[] { new DateOnly(2026, 10, 31) });
    }

    [Test]
    public async Task A_first_date_that_is_yesterday_in_UTC_but_today_locally_is_not_in_the_past()
    {
        // 02:00Z on 2 Nov is still 1 Nov 23:00 in Sao Paulo.
        var now = new DateTimeOffset(2026, 11, 2, 2, 0, 0, TimeSpan.Zero);
        var schedule = new RecurrenceSchedule(RecurrenceFrequency.Monthly, new DateOnly(2026, 11, 1), new TimeOnly(23, 30));

        var template = RecurringExpense.Create(Household, Group, "Rent", Funding, Split, schedule, SaoPaulo, false, now, Ana);

        await Assert.That(template.NextOccurrenceAt).IsEqualTo(new DateTimeOffset(2026, 11, 2, 2, 30, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task New_templates_and_updates_read_their_schedule_in_the_household_zone()
    {
        var template = Create(Monthly(2026, 12, 1));

        template.Update("Rent", Funding, Split, Monthly(2026, 12, 5), Utc, Now, Ana);

        await Assert.That(template.DomainEvents.OfType<RecurringExpenseCreated>().Single().LocalTime).IsTrue();
        await Assert.That(template.DomainEvents.OfType<RecurringExpenseUpdated>().Single().LocalTime).IsTrue();
    }

    [Test]
    public async Task Replaying_a_localized_schedule_changes_it_and_keeps_the_next_instant()
    {
        var created = new RecurringExpenseCreated(
            Guid.NewGuid(), Now, Guid.NewGuid(), Household, Group, "Rent", Funding, Split,
            new RecurrenceSchedule(RecurrenceFrequency.Monthly, new DateOnly(2026, 12, 1), new TimeOnly(1, 0)),
            new DateTimeOffset(2026, 12, 1, 1, 0, 0, TimeSpan.Zero), Ana);
        var template = RecurringExpense.Create(created);
        var local = new RecurrenceSchedule(RecurrenceFrequency.Monthly, new DateOnly(2026, 11, 30), new TimeOnly(22, 0));

        template.Apply(new RecurringExpenseScheduleLocalized(Guid.NewGuid(), Now, template.Id, Household, local));

        await Assert.That(template.Schedule).IsEqualTo(local);
        await Assert.That(template.NextOccurrenceAt).IsEqualTo(new DateTimeOffset(2026, 12, 1, 1, 0, 0, TimeSpan.Zero));
        await Assert.That(template.Schedule.In(SaoPaulo).At(0)).IsEqualTo(template.NextOccurrenceAt);
    }

    [Test]
    public async Task A_kept_instant_before_the_zones_occurrence_on_the_same_date_is_due_once()
    {
        // Scheduled at 22:00Z on the 4th, then the household moves to São Paulo: the kept instant is 19:00 there and
        // the zone's own 22:00 on the 4th (01:00Z on the 5th) is the same occurrence.
        var template = Create(Monthly(2026, 12, 4, hour: 22));
        var now = new DateTimeOffset(2026, 12, 5, 2, 0, 0, TimeSpan.Zero);

        var due = template.DueOccurrences(now, SaoPaulo);
        foreach (var date in due) template.MarkOccurred(date, now, SaoPaulo);

        await Assert.That(due).IsEquivalentTo(new[] { new DateOnly(2026, 12, 4) });
        await Assert.That(template.NextOccurrenceAt).IsEqualTo(new DateTimeOffset(2027, 1, 5, 1, 0, 0, TimeSpan.Zero));
    }
}
