using HouseholdApp.Application.Modules.Expenses.Domain;

namespace HouseholdApp.UnitTests.Modules.Expenses.Domain;

public sealed class InstallmentPlanTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly Guid GroupId = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Leo = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 10);

    // R$ 1.500,00 in 5x, Ana pays, 50/50.
    private static InstallmentPlan NewPlan(DateOnly? firstDate = null, long total = 150000, int count = 5) =>
        InstallmentPlan.Create(
            HouseholdId, GroupId, "Car repair", Ana, total, count,
            purchaseDate: firstDate ?? Today, firstDate: firstDate ?? Today,
            allocations: [new Allocation(Ana, total / 2), new Allocation(Leo, total - total / 2)],
            today: Today, now: Now, performedByUserId: Ana);

    [Test]
    public async Task Create_raises_created_with_the_catch_up_count()
    {
        var plan = NewPlan(firstDate: new DateOnly(2026, 9, 10)); // Sep, Oct, Nov due

        var created = (InstallmentPlanCreated)plan.DomainEvents.Single();
        await Assert.That(created.CatchUpCount).IsEqualTo(3);
        await Assert.That(plan.Status).IsEqualTo(InstallmentPlanStatus.Active);
        await Assert.That(plan.DueNumbers(Today)).IsEquivalentTo(new[] { 1, 2, 3 });
    }

    [Test]
    [Arguments(1)]
    [Arguments(49)]
    public async Task Create_rejects_counts_out_of_range(int count)
    {
        await Assert.That(() => NewPlan(count: count)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Create_rejects_allocations_that_do_not_sum_to_the_total()
    {
        await Assert.That(() => InstallmentPlan.Create(
            HouseholdId, GroupId, "X", Ana, 150000, 5, Today, Today,
            [new Allocation(Ana, 100)], Today, Now, Ana)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Create_rejects_shares_smaller_than_a_cent_per_installment()
    {
        await Assert.That(() => InstallmentPlan.Create(
            HouseholdId, GroupId, "X", Ana, 5, 5, Today, Today,
            [new Allocation(Ana, 4), new Allocation(Leo, 1)], Today, Now, Ana)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task BuildInstallment_records_a_linked_expense_on_its_due_date()
    {
        var plan = NewPlan();

        var expense = plan.BuildInstallment(2, Now, coveredByPlanEvent: false);

        var recorded = (ExpenseRecorded)expense.DomainEvents.Single();
        await Assert.That(expense.Id).IsEqualTo(InstallmentSchedule.InstallmentExpenseId(plan.Id, 2));
        await Assert.That(recorded.Date).IsEqualTo(new DateTimeOffset(2026, 12, 10, 0, 0, 0, TimeSpan.Zero));
        await Assert.That(recorded.FundingSources.Single()).IsEqualTo(new FundingSource(Ana, 30000));
        await Assert.That(recorded.Installment).IsEqualTo(new InstallmentLink(plan.Id, 2, 5));
        await Assert.That(recorded.PerformedByUserId).IsEqualTo(Ana);
        await Assert.That(recorded.CoveredByPlanEvent).IsFalse();
    }

    [Test]
    public async Task Update_appends_a_segment_from_the_given_number()
    {
        var plan = NewPlan();
        plan.ClearEvents();

        plan.Update("Car repair (shop)", Leo, [new Allocation(Leo, 90000)], fromNumber: 3, Now, Ana);

        var updated = (InstallmentPlanUpdated)plan.DomainEvents.Single();
        await Assert.That(updated.Segment.FromNumber).IsEqualTo(3);
        await Assert.That(updated.OldDescription).IsEqualTo("Car repair");
        await Assert.That(plan.PayerId).IsEqualTo(Leo);
        var third = plan.BuildInstallment(3, Now, false);
        await Assert.That(((ExpenseRecorded)third.DomainEvents.Single()).Allocations)
            .IsEquivalentTo(new[] { new Allocation(Leo, 30000) });
    }

    [Test]
    public async Task Update_rejects_allocations_that_do_not_match_the_remaining_amount()
    {
        var plan = NewPlan();
        await Assert.That(() => plan.Update("X", Ana, [new Allocation(Ana, 150000)], 3, Now, Ana))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task PayEarly_records_one_expense_split_by_remaining_share_and_ends_the_plan()
    {
        var plan = NewPlan();
        plan.ClearEvents();

        var payoff = plan.PayEarly(fromNumber: 3, payoffCents: 85000, "Car repair (paid early 3–5/5)", Today, Now, Ana);

        var recorded = (ExpenseRecorded)payoff.DomainEvents.Single();
        await Assert.That(recorded.Allocations)
            .IsEquivalentTo(new[] { new Allocation(Ana, 42500), new Allocation(Leo, 42500) });
        await Assert.That(recorded.Installment).IsEqualTo(new InstallmentLink(plan.Id, 3, 5, ThroughNumber: 5));
        await Assert.That(recorded.CoveredByPlanEvent).IsTrue();
        var paid = (InstallmentPlanPaidEarly)plan.DomainEvents.Single();
        await Assert.That(paid.RemainingCents).IsEqualTo(90000L);
        await Assert.That(plan.Status).IsEqualTo(InstallmentPlanStatus.PaidEarly);
    }

    [Test]
    [Arguments(0L)]
    [Arguments(90001L)]
    public async Task PayEarly_rejects_amounts_outside_zero_and_remaining(long cents)
    {
        var plan = NewPlan();
        await Assert.That(() => plan.PayEarly(3, cents, "X", Today, Now, Ana)).Throws<InvalidOperationException>();
    }

    private static InstallmentPlan EndedPlan(InstallmentPlanStatus status)
    {
        var plan = NewPlan();
        switch (status)
        {
            case InstallmentPlanStatus.Cancelled: plan.Cancel([], Now, Ana); break;
            case InstallmentPlanStatus.PaidEarly: plan.PayEarly(3, 100, "X", Today, Now, Ana); break;
            case InstallmentPlanStatus.Completed: plan.Complete(Now); break;
        }
        return plan;
    }

    [Test]
    [Arguments(InstallmentPlanStatus.Cancelled)]
    [Arguments(InstallmentPlanStatus.PaidEarly)]
    [Arguments(InstallmentPlanStatus.Completed)]
    public async Task Ended_plans_reject_every_action(InstallmentPlanStatus status)
    {
        var plan = EndedPlan(status);

        await Assert.That(plan.Status).IsEqualTo(status);
        await Assert.That(() => plan.Update("X", Ana, [new Allocation(Ana, 90000)], 3, Now, Ana))
            .Throws<InvalidOperationException>().WithMessageContaining("has ended");
        await Assert.That(() => plan.PayEarly(3, 100, "X", Today, Now, Ana))
            .Throws<InvalidOperationException>().WithMessageContaining("has ended");
        await Assert.That(() => plan.Cancel([], Now, Ana))
            .Throws<InvalidOperationException>().WithMessageContaining("has ended");
    }

    [Test]
    public async Task Update_and_PayEarly_reject_numbers_outside_the_plan()
    {
        var plan = NewPlan();
        await Assert.That(() => plan.Update("X", Ana, [new Allocation(Ana, 90000)], 0, Now, Ana))
            .Throws<InvalidOperationException>().WithMessageContaining("start at 1");
        await Assert.That(() => plan.Update("X", Ana, [new Allocation(Ana, 90000)], 6, Now, Ana))
            .Throws<InvalidOperationException>().WithMessageContaining("no installments left to change");
        await Assert.That(() => plan.PayEarly(0, 100, "X", Today, Now, Ana))
            .Throws<InvalidOperationException>().WithMessageContaining("start at 1");
        await Assert.That(() => plan.PayEarly(6, 100, "X", Today, Now, Ana))
            .Throws<InvalidOperationException>().WithMessageContaining("no installments left to pay");
    }

    [Test]
    public async Task Update_rejects_shares_smaller_than_a_cent_per_installment()
    {
        var plan = NewPlan();
        // Installments 3-5 total 90000 cents; Leo's 1 cent of weight is below a cent per installment.
        await Assert.That(() => plan.Update("X", Ana, [new Allocation(Ana, 89999), new Allocation(Leo, 1)], 3, Now, Ana))
            .Throws<InvalidOperationException>().WithMessageContaining("at least one cent per installment");
    }

    [Test]
    public async Task Complete_only_moves_an_active_plan()
    {
        var plan = NewPlan();
        plan.ClearEvents();
        plan.Complete(Now);
        plan.Complete(Now);

        await Assert.That(plan.Status).IsEqualTo(InstallmentPlanStatus.Completed);
        await Assert.That(plan.DomainEvents.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Replaying_events_rebuilds_the_same_state()
    {
        var plan = NewPlan();
        plan.Update("Renamed", Leo, [new Allocation(Leo, 90000)], 3, Now, Ana);
        plan.PayEarly(3, 100, "Renamed (paid early)", Today, Now, Ana);

        var replayed = InstallmentPlan.Create((InstallmentPlanCreated)plan.DomainEvents[0]);
        replayed.Apply((InstallmentPlanUpdated)plan.DomainEvents[1]);
        replayed.Apply((InstallmentPlanPaidEarly)plan.DomainEvents[2]);
        await Assert.That(replayed.Status).IsEqualTo(InstallmentPlanStatus.PaidEarly);
        await Assert.That(replayed.Description).IsEqualTo("Renamed");
        await Assert.That(replayed.PayerId).IsEqualTo(Leo);
        await Assert.That(replayed.Segments.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Replaying_a_completed_event_ends_an_active_plan()
    {
        var plan = NewPlan();
        var replayed = InstallmentPlan.Create((InstallmentPlanCreated)plan.DomainEvents[0]);

        replayed.Apply(new InstallmentPlanCompleted(Guid.NewGuid(), Now, plan.Id, HouseholdId));

        await Assert.That(replayed.Status).IsEqualTo(InstallmentPlanStatus.Completed);
    }
}
