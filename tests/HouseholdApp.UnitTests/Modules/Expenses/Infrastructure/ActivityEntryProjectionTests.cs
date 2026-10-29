using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

namespace HouseholdApp.UnitTests.Modules.Expenses.Infrastructure;

public sealed class ActivityEntryProjectionTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly Guid ExpenseId = Guid.NewGuid();
    private static readonly Guid GroupId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Test]
    public async Task Map_ExpenseRecorded_without_correlation_yields_Added()
    {
        var evt = new ExpenseRecorded(
            Guid.NewGuid(), Now, ExpenseId, HouseholdId, GroupId, "Groceries", Now,
            [new FundingSource(ActorId, 1000)], [new Allocation(ActorId, 1000)], ActorId);

        var entry = ActivityEntryProjection.MapRecorded(Guid.NewGuid(), evt, groupName: "Casa")!;

        await Assert.That(entry.Kind).IsEqualTo(ActivityKind.Added);
        await Assert.That(entry.ActorUserId).IsEqualTo(ActorId);
        await Assert.That(entry.Description).IsEqualTo("Groceries");
        await Assert.That(entry.GroupName).IsEqualTo("Casa");
        await Assert.That(entry.ViewerDeltaCents[ActorId]).IsEqualTo(0L);
    }

    [Test]
    public async Task Map_ExpenseRecorded_with_RecurringExpenseId_yields_RecurringAdded()
    {
        var evt = new ExpenseRecorded(
            Guid.NewGuid(), Now, ExpenseId, HouseholdId, GroupId, "Aluguel", Now,
            [new FundingSource(ActorId, 1000)], [new Allocation(ActorId, 1000)], ActorId,
            RecurringExpenseId: Guid.NewGuid());

        var entry = ActivityEntryProjection.MapRecorded(Guid.NewGuid(), evt, groupName: "Casa")!;

        await Assert.That(entry.Kind).IsEqualTo(ActivityKind.RecurringAdded);
    }

    [Test]
    public async Task Map_ExpenseRecorded_with_CorrectedFromExpenseId_yields_Edited()
    {
        var evt = new ExpenseRecorded(
            Guid.NewGuid(), Now, ExpenseId, HouseholdId, GroupId, "Aluguel (corrected)", Now,
            [new FundingSource(ActorId, 1200)], [new Allocation(ActorId, 1200)], ActorId,
            CorrectedFromExpenseId: Guid.NewGuid());

        var entry = ActivityEntryProjection.MapRecorded(Guid.NewGuid(), evt, groupName: "Casa")!;

        await Assert.That(entry.Kind).IsEqualTo(ActivityKind.Edited);
    }

    [Test]
    public async Task Map_ExpenseVoided_with_no_correlation_yields_Removed_with_reversed_delta()
    {
        var payer = Guid.NewGuid();
        var debtor = Guid.NewGuid();
        var evt = new ExpenseVoided(
            Guid.NewGuid(), Now, ExpenseId, HouseholdId, "mistake",
            [new FundingSource(payer, 1000)], [new Allocation(payer, 500), new Allocation(debtor, 500)],
            ActorId, "Groceries");

        var entry = ActivityEntryProjection.MapVoided(Guid.NewGuid(), evt, GroupId);

        await Assert.That(entry).IsNotNull();
        await Assert.That(entry!.Kind).IsEqualTo(ActivityKind.Removed);
        await Assert.That(entry.Description).IsEqualTo("Groceries");
        await Assert.That(entry.ViewerDeltaCents[payer]).IsEqualTo(-500L);
        await Assert.That(entry.ViewerDeltaCents[debtor]).IsEqualTo(500L);
    }

    [Test]
    public async Task Map_ExpenseVoided_with_CorrectedByExpenseId_is_suppressed()
    {
        var evt = new ExpenseVoided(
            Guid.NewGuid(), Now, ExpenseId, HouseholdId, "Edited",
            [new FundingSource(ActorId, 1000)], [new Allocation(ActorId, 1000)],
            ActorId, "Groceries", CorrectedByExpenseId: Guid.NewGuid());

        var entry = ActivityEntryProjection.MapVoided(Guid.NewGuid(), evt, GroupId);

        await Assert.That(entry).IsNull();
    }

    [Test]
    public async Task Map_SettlementRecorded_yields_Settlement_with_no_delta()
    {
        var payer = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        var evt = new SettlementRecorded(
            Guid.NewGuid(), Now, Guid.NewGuid(), HouseholdId, payer, recipient, 1500, Now, ActorId);

        var entry = ActivityEntryProjection.MapSettlement(Guid.NewGuid(), evt);

        await Assert.That(entry.Kind).IsEqualTo(ActivityKind.Settlement);
        await Assert.That(entry.SettlementPayerId).IsEqualTo(payer);
        await Assert.That(entry.SettlementRecipientId).IsEqualTo(recipient);
        await Assert.That(entry.ViewerDeltaCents).IsEmpty();
    }

    [Test]
    public async Task Map_ExpenseRecorded_keeps_the_group_id()
    {
        var evt = new ExpenseRecorded(
            Guid.NewGuid(), Now, ExpenseId, HouseholdId, GroupId, "Groceries", Now,
            [new FundingSource(ActorId, 1000)], [new Allocation(ActorId, 1000)], ActorId);

        await Assert.That(ActivityEntryProjection.MapRecorded(Guid.NewGuid(), evt, "Casa")!.GroupId).IsEqualTo(GroupId);
    }

    [Test]
    public async Task Map_household_settlement_uses_the_cash_total_and_group_names()
    {
        var payer = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        var id = Guid.NewGuid();
        var evt = new SettlementRecorded(Guid.NewGuid(), Now, id, HouseholdId, payer, recipient, 100, Now, ActorId,
            GroupId, HouseholdSettlementId: id, HouseholdSettlementCents: 70,
            HouseholdSettlementGroupIds: [GroupId, Guid.NewGuid()]);

        var other = Guid.NewGuid();
        var entry = ActivityEntryProjection.MapHouseholdSettlement(Guid.NewGuid(), evt, [(GroupId, "Trip"), (other, "Home")]);

        await Assert.That(entry.Kind).IsEqualTo(ActivityKind.HouseholdSettlement);
        await Assert.That(entry.SettlementCents).IsEqualTo(70L);
        await Assert.That(entry.GroupNames).IsEquivalentTo(new[] { "Trip", "Home" });
        await Assert.That(entry.GroupIds).IsEquivalentTo(new[] { GroupId, other });
        await Assert.That(entry.GroupId).IsNull();
    }

    [Test]
    public async Task Map_rename_keeps_both_names_on_the_entry()
    {
        var evt = new ExpenseGroupUpdated(Guid.NewGuid(), Now, GroupId, HouseholdId,
            "Trip", "Japan 2027", null, null, "✈️", "🗾", ActorId);

        var entry = ActivityEntryProjection.MapGroupRenamed(Guid.NewGuid(), evt);

        await Assert.That(entry.Kind).IsEqualTo(ActivityKind.GroupRenamed);
        await Assert.That((entry.RenamedFrom, entry.RenamedTo)).IsEqualTo(("Trip", "Japan 2027"));
        await Assert.That(entry.GroupId).IsEqualTo(GroupId);
        await Assert.That(entry.ActorUserId).IsEqualTo(ActorId);
    }

    [Test]
    public async Task Map_group_deleted_names_the_group()
    {
        var evt = new ExpenseGroupDeleted(Guid.NewGuid(), Now, GroupId, HouseholdId, "Trip", ActorId);

        var entry = ActivityEntryProjection.MapGroupLifecycle(Guid.NewGuid(), ActivityKind.GroupDeleted,
            evt.OccurredAt, evt.GroupId, evt.HouseholdId, evt.GroupName, evt.PerformedByUserId);

        await Assert.That(entry.Kind).IsEqualTo(ActivityKind.GroupDeleted);
        await Assert.That(entry.GroupId).IsEqualTo(GroupId);
        await Assert.That(entry.GroupName).IsEqualTo("Trip");
    }

    private static readonly Guid PlanId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();

    private static ExpenseRecorded Installment(int number, bool covered = false, Guid? correctedFrom = null) => new(
        Guid.NewGuid(), Now, ExpenseId, HouseholdId, GroupId, "Car repair", Now,
        [new FundingSource(ActorId, 30000)], [new Allocation(ActorId, 15000), new Allocation(OtherId, 15000)],
        ActorId, CorrectedFromExpenseId: correctedFrom, Installment: new InstallmentLink(PlanId, number, 5),
        CoveredByPlanEvent: covered);

    [Test]
    public async Task Map_sweep_installment_yields_InstallmentAdded_tagged_with_the_plan()
    {
        var entry = ActivityEntryProjection.MapRecorded(Guid.NewGuid(), Installment(2), "Car")!;

        await Assert.That(entry.Kind).IsEqualTo(ActivityKind.InstallmentAdded);
        await Assert.That(entry.InstallmentPlanId).IsEqualTo(PlanId);
        await Assert.That(entry.InstallmentNumber).IsEqualTo(2);
        await Assert.That(entry.InstallmentCount).IsEqualTo(5);
        await Assert.That(entry.AmountCents).IsEqualTo(30000L);
        await Assert.That(entry.ViewerDeltaCents[OtherId]).IsEqualTo(-15000L);
    }

    [Test]
    public async Task Map_covered_expense_yields_no_row()
    {
        await Assert.That(ActivityEntryProjection.MapRecorded(Guid.NewGuid(), Installment(1, covered: true), "Car")).IsNull();
    }

    [Test]
    public async Task Map_edited_installment_stays_Edited_and_keeps_the_plan_tag()
    {
        var entry = ActivityEntryProjection.MapRecorded(Guid.NewGuid(), Installment(2, correctedFrom: Guid.NewGuid()), "Car")!;
        await Assert.That(entry.Kind).IsEqualTo(ActivityKind.Edited);
        await Assert.That(entry.InstallmentPlanId).IsEqualTo(PlanId);
    }

    [Test]
    public async Task Map_plan_created_carries_the_catch_up_delta()
    {
        var created = new InstallmentPlanCreated(Guid.NewGuid(), Now, PlanId, HouseholdId, GroupId, "Car repair",
            ActorId, 150000, 0, 5, new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 10),
            [new Allocation(ActorId, 75000), new Allocation(OtherId, 75000)], CatchUpCount: 2, ActorId);

        var entry = ActivityEntryProjection.MapPlanCreated(Guid.NewGuid(), created, "Car");

        await Assert.That(entry.Kind).IsEqualTo(ActivityKind.InstallmentPlanCreated);
        await Assert.That(entry.InstallmentCount).IsEqualTo(5);
        await Assert.That(entry.AmountCents).IsEqualTo(30000L);
        await Assert.That(entry.ViewerDeltaCents[OtherId]).IsEqualTo(-30000L);
        await Assert.That(entry.ViewerDeltaCents[ActorId]).IsEqualTo(30000L);
    }

    [Test]
    public async Task Map_plan_created_with_a_down_payment_carries_it_and_counts_it_in_the_catch_up_delta()
    {
        var created = new InstallmentPlanCreated(Guid.NewGuid(), Now, PlanId, HouseholdId, GroupId, "Car repair",
            ActorId, 150000, 50000, 5, new DateOnly(2026, 10, 10), new DateOnly(2026, 11, 10),
            [new Allocation(ActorId, 75000), new Allocation(OtherId, 75000)], CatchUpCount: 1, ActorId);

        var entry = ActivityEntryProjection.MapPlanCreated(Guid.NewGuid(), created, "Car");

        await Assert.That(entry.DownPaymentCents).IsEqualTo(50000L);
        await Assert.That(entry.AmountCents).IsEqualTo(20000L);
        await Assert.That(entry.ViewerDeltaCents[OtherId]).IsEqualTo(-25000L); // the down payment, split evenly
        await Assert.That(entry.ViewerDeltaCents[ActorId]).IsEqualTo(25000L);
    }

    [Test]
    public async Task Map_plan_created_without_a_down_payment_leaves_it_null()
    {
        var created = new InstallmentPlanCreated(Guid.NewGuid(), Now, PlanId, HouseholdId, GroupId, "Car repair",
            ActorId, 150000, 0, 5, new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 10),
            [new Allocation(ActorId, 75000), new Allocation(OtherId, 75000)], CatchUpCount: 0, ActorId);

        await Assert.That(ActivityEntryProjection.MapPlanCreated(Guid.NewGuid(), created, "Car").DownPaymentCents).IsNull();
    }

    [Test]
    public async Task Map_sweep_created_down_payment_is_an_installment_added_row_numbered_zero()
    {
        var recorded = new ExpenseRecorded(Guid.NewGuid(), Now, ExpenseId, HouseholdId, GroupId, "Car repair", Now,
            [new FundingSource(ActorId, 50000)], [new Allocation(ActorId, 25000), new Allocation(OtherId, 25000)],
            ActorId, Installment: new InstallmentLink(PlanId, 0, 5));

        var entry = ActivityEntryProjection.MapRecorded(Guid.NewGuid(), recorded, "Car");

        await Assert.That(entry!.Kind).IsEqualTo(ActivityKind.InstallmentAdded);
        await Assert.That(entry.InstallmentNumber).IsEqualTo(0);
        await Assert.That(entry.AmountCents).IsEqualTo(50000L);
    }

    [Test]
    public async Task Map_paid_early_and_cancelled_carry_their_deltas()
    {
        var paid = new InstallmentPlanPaidEarly(Guid.NewGuid(), Now, PlanId, HouseholdId, GroupId, "Car repair",
            Guid.NewGuid(), 3, 5, 90000, 85000, ActorId,
            [new Allocation(ActorId, 42500), new Allocation(OtherId, 42500)], ActorId);
        var cancelled = new InstallmentPlanCancelled(Guid.NewGuid(), Now, PlanId, HouseholdId, GroupId, "Car repair",
            [new VoidedInstallment(Guid.NewGuid(), 1, [new FundingSource(ActorId, 30000)],
                [new Allocation(ActorId, 15000), new Allocation(OtherId, 15000)])], ActorId);

        var paidEntry = ActivityEntryProjection.MapPlanPaidEarly(Guid.NewGuid(), paid, "Car");
        var cancelEntry = ActivityEntryProjection.MapPlanCancelled(Guid.NewGuid(), cancelled, "Car");

        await Assert.That(paidEntry.InstallmentNumber).IsEqualTo(3);
        await Assert.That(paidEntry.AmountCents).IsEqualTo(85000L);
        await Assert.That(paidEntry.ReferenceCents).IsEqualTo(90000L);
        await Assert.That(paidEntry.ViewerDeltaCents[OtherId]).IsEqualTo(-42500L);
        await Assert.That(cancelEntry.VoidedCount).IsEqualTo(1);
        await Assert.That(cancelEntry.ViewerDeltaCents[OtherId]).IsEqualTo(15000L);
    }

    [Test]
    public async Task Map_covered_void_yields_no_row_and_plain_installment_void_keeps_the_plan_tag()
    {
        ExpenseVoided Void(bool covered) => new(Guid.NewGuid(), Now, ExpenseId, HouseholdId, null,
            [new FundingSource(ActorId, 30000)], [new Allocation(ActorId, 15000), new Allocation(OtherId, 15000)],
            ActorId, "Car repair", Installment: new InstallmentLink(PlanId, 2, 5), CoveredByPlanEvent: covered);

        await Assert.That(ActivityEntryProjection.MapVoided(Guid.NewGuid(), Void(covered: true), GroupId)).IsNull();

        var entry = ActivityEntryProjection.MapVoided(Guid.NewGuid(), Void(covered: false), GroupId)!;
        await Assert.That(entry.Kind).IsEqualTo(ActivityKind.Removed);
        await Assert.That(entry.InstallmentPlanId).IsEqualTo(PlanId);
        await Assert.That(entry.InstallmentNumber).IsEqualTo(2);
    }
}
