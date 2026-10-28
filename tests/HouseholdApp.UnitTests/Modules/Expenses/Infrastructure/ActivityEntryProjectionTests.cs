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

        var entry = ActivityEntryProjection.MapRecorded(Guid.NewGuid(), evt, groupName: "Casa");

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

        var entry = ActivityEntryProjection.MapRecorded(Guid.NewGuid(), evt, groupName: "Casa");

        await Assert.That(entry.Kind).IsEqualTo(ActivityKind.RecurringAdded);
    }

    [Test]
    public async Task Map_ExpenseRecorded_with_CorrectedFromExpenseId_yields_Edited()
    {
        var evt = new ExpenseRecorded(
            Guid.NewGuid(), Now, ExpenseId, HouseholdId, GroupId, "Aluguel (corrected)", Now,
            [new FundingSource(ActorId, 1200)], [new Allocation(ActorId, 1200)], ActorId,
            CorrectedFromExpenseId: Guid.NewGuid());

        var entry = ActivityEntryProjection.MapRecorded(Guid.NewGuid(), evt, groupName: "Casa");

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

        await Assert.That(ActivityEntryProjection.MapRecorded(Guid.NewGuid(), evt, "Casa").GroupId).IsEqualTo(GroupId);
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

        var entry = ActivityEntryProjection.MapHouseholdSettlement(Guid.NewGuid(), evt, ["Trip", "Home"]);

        await Assert.That(entry.Kind).IsEqualTo(ActivityKind.HouseholdSettlement);
        await Assert.That(entry.SettlementCents).IsEqualTo(70L);
        await Assert.That(entry.GroupNames).IsEquivalentTo(new[] { "Trip", "Home" });
        await Assert.That(entry.GroupId).IsNull();
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
}
