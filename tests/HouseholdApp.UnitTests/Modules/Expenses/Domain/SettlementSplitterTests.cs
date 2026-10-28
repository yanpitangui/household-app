using HouseholdApp.Application.Modules.Expenses.Domain;

namespace HouseholdApp.UnitTests.Modules.Expenses.Domain;

public sealed class SettlementSplitterTests
{
    // Ordered ids so tie-breaks are deterministic: A < B < C.
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    [Test]
    public async Task Full_payment_of_a_single_group_is_one_cash_leg()
    {
        var split = SettlementSplitter.Split([new GroupDebt(A, 100)], 100);

        await Assert.That(split.Error).IsNull();
        await Assert.That(split.OwedCents).IsEqualTo(100L);
        await Assert.That(split.Legs).IsEquivalentTo(new[] { new SettlementLeg(A, 100, false) });
    }

    [Test]
    public async Task Full_net_payment_offsets_opposite_groups_and_closes_all()
    {
        // Trip: payer owes 100. Home: recipient owes payer 30. Net 70.
        var split = SettlementSplitter.Split([new GroupDebt(A, 100), new GroupDebt(B, -30)], 70);

        await Assert.That(split.Error).IsNull();
        await Assert.That(split.Legs[0]).IsEqualTo(new SettlementLeg(A, 100, false));
        await Assert.That(split.Legs[1]).IsEqualTo(new SettlementLeg(B, 30, true));
        await Assert.That(split.Legs.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Partial_payment_still_closes_offsets_and_leaves_debt_open()
    {
        var split = SettlementSplitter.Split([new GroupDebt(A, 100), new GroupDebt(B, -30)], 40);

        await Assert.That(split.Legs[0]).IsEqualTo(new SettlementLeg(A, 70, false));
        await Assert.That(split.Legs[1]).IsEqualTo(new SettlementLeg(B, 30, true));
    }

    [Test]
    public async Task Cash_goes_to_the_largest_debt_first()
    {
        var split = SettlementSplitter.Split([new GroupDebt(A, 50), new GroupDebt(B, 80)], 100);

        await Assert.That(split.Legs[0]).IsEqualTo(new SettlementLeg(B, 80, false));
        await Assert.That(split.Legs[1]).IsEqualTo(new SettlementLeg(A, 20, false));
        await Assert.That(split.Legs.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Equal_debts_break_ties_by_group_id()
    {
        var split = SettlementSplitter.Split([new GroupDebt(C, 50), new GroupDebt(A, 50)], 60);

        await Assert.That(split.Legs[0]).IsEqualTo(new SettlementLeg(A, 50, false));
        await Assert.That(split.Legs[1]).IsEqualTo(new SettlementLeg(C, 10, false));
    }

    [Test]
    public async Task Zero_balance_groups_get_no_leg()
    {
        var split = SettlementSplitter.Split([new GroupDebt(A, 100), new GroupDebt(B, 0)], 100);

        await Assert.That(split.Legs.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Paying_more_than_owed_is_rejected()
    {
        var split = SettlementSplitter.Split([new GroupDebt(A, 100), new GroupDebt(B, -30)], 71);

        await Assert.That(split.Error).IsEqualTo(SettlementSplitError.ExceedsOwed);
        await Assert.That(split.OwedCents).IsEqualTo(70L);
        await Assert.That(split.Legs).IsEmpty();
    }

    [Test]
    public async Task Payer_who_is_owed_gets_wrong_direction()
    {
        var split = SettlementSplitter.Split([new GroupDebt(A, -50)], 10);

        await Assert.That(split.Error).IsEqualTo(SettlementSplitError.WrongDirection);
    }

    [Test]
    public async Task No_balance_is_nothing_owed()
    {
        await Assert.That(SettlementSplitter.Split([], 10).Error).IsEqualTo(SettlementSplitError.NothingOwed);
        await Assert.That(SettlementSplitter.Split([new GroupDebt(A, 0)], 10).Error).IsEqualTo(SettlementSplitError.NothingOwed);
    }

    [Test]
    public async Task Non_positive_amount_is_rejected()
    {
        var split = SettlementSplitter.Split([new GroupDebt(A, 100)], 0);

        await Assert.That(split.Error).IsEqualTo(SettlementSplitError.AmountNotPositive);
        await Assert.That(split.OwedCents).IsEqualTo(100L);
    }
}
