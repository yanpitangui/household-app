using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using Marten;
using NSubstitute;

namespace HouseholdApp.UnitTests.Modules.Expenses.Infrastructure;

public sealed class HouseholdLedgerProjectionTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Carol = Guid.NewGuid();
    private static readonly Guid GroupA = Guid.NewGuid();
    private static readonly Guid GroupB = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly HouseholdLedgerProjection _projection = new();

    private static HouseholdLedger EmptyLedger() => new() { Id = HouseholdId };

    private static ExpenseRecorded Expense(
        IReadOnlyList<FundingSource> funding,
        IReadOnlyList<Allocation> allocations,
        Guid? groupId = null) =>
        new(Guid.NewGuid(), Now, Guid.NewGuid(), HouseholdId, groupId ?? GroupA,
            "Test", Now, funding, allocations);

    private static SettlementRecorded Settlement(Guid payer, Guid recipient, long cents, Guid? groupId = null) =>
        new(Guid.NewGuid(), Now, Guid.NewGuid(), HouseholdId, payer, recipient, cents, Now,
            ExpenseGroupId: groupId ?? GroupA);

    private static long PairCents(HouseholdLedger ledger, Guid u1, Guid u2, Guid? groupId = null)
    {
        var lo = u1 < u2 ? u1 : u2;
        var hi = u1 < u2 ? u2 : u1;
        var sign = u1 < u2 ? 1 : -1;
        var group = groupId ?? GroupA;
        var entry = ledger.Pairs.FirstOrDefault(p => p.GroupId == group && p.UserId1 == lo && p.UserId2 == hi);
        return entry is null ? 0 : sign * entry.Cents;
    }

    private static readonly IQuerySession NoSession = Substitute.For<IQuerySession>();

    [Test]
    public async Task Single_payer_even_split_creates_correct_pair()
    {
        var ledger = EmptyLedger();
        _projection.Apply(Expense(
            [new FundingSource(Alice, 1000)],
            [new Allocation(Alice, 500), new Allocation(Bob, 500)]), ledger);

        await Assert.That(PairCents(ledger, Alice, Bob)).IsEqualTo(500L);
    }

    [Test]
    public async Task Single_payer_uneven_split_creates_correct_pair()
    {
        var ledger = EmptyLedger();
        _projection.Apply(Expense(
            [new FundingSource(Alice, 900)],
            [new Allocation(Alice, 600), new Allocation(Bob, 300)]), ledger);

        await Assert.That(PairCents(ledger, Alice, Bob)).IsEqualTo(300L);
    }

    [Test]
    public async Task Multiple_expenses_accumulate()
    {
        var ledger = EmptyLedger();
        _projection.Apply(Expense(
            [new FundingSource(Alice, 1000)],
            [new Allocation(Alice, 500), new Allocation(Bob, 500)]), ledger);

        _projection.Apply(Expense(
            [new FundingSource(Bob, 600)],
            [new Allocation(Alice, 300), new Allocation(Bob, 300)]), ledger);

        await Assert.That(PairCents(ledger, Alice, Bob)).IsEqualTo(200L);
    }

    [Test]
    public async Task Three_way_split_single_payer()
    {
        var ledger = EmptyLedger();
        _projection.Apply(Expense(
            [new FundingSource(Alice, 300)],
            [new Allocation(Alice, 100), new Allocation(Bob, 100), new Allocation(Carol, 100)]), ledger);

        await Assert.That(PairCents(ledger, Alice, Bob)).IsEqualTo(100L);
        await Assert.That(PairCents(ledger, Alice, Carol)).IsEqualTo(100L);
        await Assert.That(PairCents(ledger, Bob, Carol)).IsEqualTo(0L);
    }

    private static readonly Guid Dave = Guid.NewGuid();

    // What everyone else owes the user in the group, summed over their pairs; negative when the user owes.
    private static long NetOf(HouseholdLedger ledger, Guid user, Guid? groupId = null) =>
        ledger.Pairs
            .Where(p => p.GroupId == (groupId ?? GroupA))
            .Sum(p => p.UserId1 == user ? p.Cents : p.UserId2 == user ? -p.Cents : 0);

    [Test]
    public async Task Two_payers_two_debtors_keep_every_cent()
    {
        var ledger = EmptyLedger();
        // Nets: Alice +1, Bob +1, Carol -1, Dave -1. Every proportional share is half a cent.
        _projection.Apply(Expense(
            [new FundingSource(Alice, 3), new FundingSource(Bob, 3)],
            [new Allocation(Alice, 2), new Allocation(Bob, 2), new Allocation(Carol, 1), new Allocation(Dave, 1)]), ledger);

        await Assert.That(NetOf(ledger, Alice)).IsEqualTo(1L);
        await Assert.That(NetOf(ledger, Bob)).IsEqualTo(1L);
        await Assert.That(NetOf(ledger, Carol)).IsEqualTo(-1L);
        await Assert.That(NetOf(ledger, Dave)).IsEqualTo(-1L);
    }

    [Test]
    public async Task Two_payers_split_each_debt_in_proportion_to_what_they_are_owed()
    {
        var ledger = EmptyLedger();
        // Nets: Alice +500, Bob +100, Carol -300, Dave -300.
        _projection.Apply(Expense(
            [new FundingSource(Alice, 800), new FundingSource(Bob, 400)],
            [new Allocation(Alice, 300), new Allocation(Bob, 300), new Allocation(Carol, 300), new Allocation(Dave, 300)]), ledger);

        await Assert.That(PairCents(ledger, Alice, Carol)).IsEqualTo(250L);
        await Assert.That(PairCents(ledger, Alice, Dave)).IsEqualTo(250L);
        await Assert.That(PairCents(ledger, Bob, Carol)).IsEqualTo(50L);
        await Assert.That(PairCents(ledger, Bob, Dave)).IsEqualTo(50L);
        await Assert.That(PairCents(ledger, Alice, Bob)).IsEqualTo(0L);
    }

    [Test]
    public async Task Three_way_odd_cents_with_two_payers_keep_every_cent()
    {
        var ledger = EmptyLedger();
        // R$10.00 split three ways (334/333/333), paid 7.00 by Alice and 3.00 by Bob.
        // Nets: Alice +366, Bob -33, Carol -333.
        _projection.Apply(Expense(
            [new FundingSource(Alice, 700), new FundingSource(Bob, 300)],
            [new Allocation(Alice, 334), new Allocation(Bob, 333), new Allocation(Carol, 333)]), ledger);

        await Assert.That(PairCents(ledger, Alice, Bob)).IsEqualTo(33L);
        await Assert.That(PairCents(ledger, Alice, Carol)).IsEqualTo(333L);
        await Assert.That(PairCents(ledger, Bob, Carol)).IsEqualTo(0L);
    }

    [Test]
    public async Task Voiding_a_fractional_split_returns_every_pair_to_zero()
    {
        var ledger = EmptyLedger();
        var funding = new FundingSource[] { new(Alice, 3), new(Bob, 3) };
        var allocations = new Allocation[] { new(Alice, 2), new(Bob, 2), new(Carol, 1), new(Dave, 1) };
        _projection.Apply(Expense(funding, allocations), ledger);

        await _projection.Apply(new ExpenseVoided(Guid.NewGuid(), Now, Guid.NewGuid(), HouseholdId, null, funding,
            allocations, Alice, "Test", ExpenseGroupId: GroupA), ledger, NoSession);

        await Assert.That(ledger.Pairs.All(p => p.Cents == 0)).IsTrue();
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task Random_splits_keep_every_members_net_and_voids_reverse_exactly(int seed)
    {
        var random = new Random(seed);
        var members = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var ledger = EmptyLedger();
        var expected = members.ToDictionary(m => m, _ => 0L);
        var recorded = new List<ExpenseRecorded>();

        for (var i = 0; i < 200; i++)
        {
            var people = members.OrderBy(_ => random.Next()).Take(random.Next(2, members.Length + 1)).ToArray();
            var total = random.NextInt64(1, 100_000);
            var funding = RandomParts(random, total, people.Take(random.Next(1, Math.Min(3, people.Length) + 1)).ToArray())
                .Select(p => new FundingSource(p.User, p.Cents)).ToList();
            var allocations = RandomParts(random, total, people)
                .Select(p => new Allocation(p.User, p.Cents)).ToList();

            var e = Expense(funding, allocations);
            _projection.Apply(e, ledger);
            recorded.Add(e);
            foreach (var f in funding) expected[f.UserId] += f.Cents;
            foreach (var a in allocations) expected[a.UserId] -= a.Cents;
        }

        foreach (var m in members)
            await Assert.That(NetOf(ledger, m)).IsEqualTo(expected[m]);

        foreach (var e in recorded.OrderBy(_ => random.Next()))
            await _projection.Apply(new ExpenseVoided(Guid.NewGuid(), Now, e.ExpenseId, HouseholdId, null,
                e.FundingSources, e.Allocations, Alice, "Test", ExpenseGroupId: GroupA), ledger, NoSession);

        await Assert.That(ledger.Pairs.All(p => p.Cents == 0)).IsTrue();
    }

    [Test]
    public async Task Unbalanced_expense_owing_more_than_was_paid_gives_the_payer_only_what_they_paid()
    {
        var ledger = EmptyLedger();
        // Shares outgrow funding; the domain rejects this, but a rebuild must still get past such an event.
        _projection.Apply(Expense([new FundingSource(Alice, 100)], [new Allocation(Bob, 100), new Allocation(Carol, 100)]), ledger);

        await Assert.That(NetOf(ledger, Alice)).IsEqualTo(100L);
    }

    // Splits total into one positive part per user.
    private static IEnumerable<(Guid User, long Cents)> RandomParts(Random random, long total, Guid[] users)
    {
        if (total < users.Length) users = users.Take((int)total).ToArray();
        var cuts = new SortedSet<long>();
        while (cuts.Count < users.Length - 1) cuts.Add(random.NextInt64(1, total));
        var bounds = cuts.Prepend(0).Append(total).ToArray();
        return users.Select((u, i) => (u, bounds[i + 1] - bounds[i]));
    }

    [Test]
    public async Task Settlement_reduces_debt()
    {
        var ledger = EmptyLedger();
        _projection.Apply(Expense(
            [new FundingSource(Alice, 1000)],
            [new Allocation(Alice, 500), new Allocation(Bob, 500)]), ledger);

        _projection.Apply(Settlement(Bob, Alice, 300), ledger);

        await Assert.That(PairCents(ledger, Alice, Bob)).IsEqualTo(200L);
    }

    [Test]
    public async Task Settlement_full_repayment_zeroes_balance()
    {
        var ledger = EmptyLedger();
        _projection.Apply(Expense(
            [new FundingSource(Alice, 1000)],
            [new Allocation(Alice, 500), new Allocation(Bob, 500)]), ledger);

        _projection.Apply(Settlement(Bob, Alice, 500), ledger);

        await Assert.That(PairCents(ledger, Alice, Bob)).IsEqualTo(0L);
    }

    [Test]
    public async Task Voided_expense_reverses_the_balance()
    {
        var ledger = EmptyLedger();
        var funding = new FundingSource[] { new(Alice, 1000) };
        var allocations = new Allocation[] { new(Alice, 500), new(Bob, 500) };
        _projection.Apply(Expense(funding, allocations), ledger);

        await _projection.Apply(new ExpenseVoided(Guid.NewGuid(), Now, Guid.NewGuid(), HouseholdId, null, funding, allocations, Guid.NewGuid(), "Groceries", ExpenseGroupId: GroupA), ledger, NoSession);

        await Assert.That(PairCents(ledger, Alice, Bob)).IsEqualTo(0L);
    }

    [Test]
    public async Task Voided_expense_only_reverses_its_own_amount()
    {
        var ledger = EmptyLedger();
        var funding = new FundingSource[] { new(Alice, 1000) };
        var allocations = new Allocation[] { new(Alice, 500), new(Bob, 500) };
        _projection.Apply(Expense(funding, allocations), ledger);
        _projection.Apply(Expense(
            [new FundingSource(Alice, 600)],
            [new Allocation(Alice, 300), new Allocation(Bob, 300)]), ledger);

        await _projection.Apply(new ExpenseVoided(Guid.NewGuid(), Now, Guid.NewGuid(), HouseholdId, null, funding, allocations, Guid.NewGuid(), "Groceries", ExpenseGroupId: GroupA), ledger, NoSession);

        await Assert.That(PairCents(ledger, Alice, Bob)).IsEqualTo(300L);
    }

    [Test]
    public async Task Expenses_in_different_groups_keep_separate_pairs()
    {
        var ledger = EmptyLedger();
        _projection.Apply(Expense([new FundingSource(Alice, 1000)],
            [new Allocation(Alice, 500), new Allocation(Bob, 500)], GroupA), ledger);
        _projection.Apply(Expense([new FundingSource(Bob, 600)],
            [new Allocation(Alice, 300), new Allocation(Bob, 300)], GroupB), ledger);

        await Assert.That(PairCents(ledger, Alice, Bob, GroupA)).IsEqualTo(500L);
        await Assert.That(PairCents(ledger, Alice, Bob, GroupB)).IsEqualTo(-300L);
    }

    [Test]
    public async Task Settlement_without_group_is_ignored()
    {
        var ledger = EmptyLedger();
        _projection.Apply(new SettlementRecorded(Guid.NewGuid(), Now, Guid.NewGuid(), HouseholdId, Bob, Alice, 300, Now), ledger);

        await Assert.That(ledger.Pairs).IsEmpty();
    }

    [Test]
    public async Task DebtsBetween_reads_each_group_from_the_debtors_side()
    {
        var ledger = EmptyLedger();
        _projection.Apply(Expense([new FundingSource(Alice, 1000)],
            [new Allocation(Alice, 500), new Allocation(Bob, 500)], GroupA), ledger);
        _projection.Apply(Expense([new FundingSource(Bob, 600)],
            [new Allocation(Alice, 300), new Allocation(Bob, 300)], GroupB), ledger);

        var debts = ledger.DebtsBetween(debtor: Bob, creditor: Alice);

        await Assert.That(debts).IsEquivalentTo(new[] { new GroupDebt(GroupA, 500), new GroupDebt(GroupB, -300) });
        await Assert.That(ledger.DebtsBetween(Bob, Alice, new HashSet<Guid> { GroupB }))
            .IsEquivalentTo(new[] { new GroupDebt(GroupB, -300) });
    }

    private static readonly DateTimeOffset T1 = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = T1.AddHours(1);
    private static readonly DateTimeOffset T3 = T1.AddHours(2);

    private static ExpenseRecorded ExpenseAt(DateTimeOffset at, long cents, Guid payer, Guid other, Guid? groupId = null) =>
        new(Guid.NewGuid(), at, Guid.NewGuid(), HouseholdId, groupId ?? GroupA, "Test", at,
            [new FundingSource(payer, cents)], [new Allocation(payer, cents / 2), new Allocation(other, cents / 2)]);

    private static SettlementRecorded SettlementAt(DateTimeOffset at, Guid payer, Guid recipient, long cents, Guid? groupId = null) =>
        new(Guid.NewGuid(), at, Guid.NewGuid(), HouseholdId, payer, recipient, cents, at, ExpenseGroupId: groupId ?? GroupA);

    [Test]
    public async Task Full_settlement_marks_the_group_settled_at_the_settlements_time()
    {
        var ledger = EmptyLedger();
        _projection.Apply(ExpenseAt(T1, 1000, Alice, Bob), ledger);
        await Assert.That(ledger.GroupSettledAt.ContainsKey(GroupA)).IsFalse();

        _projection.Apply(SettlementAt(T2, Bob, Alice, 500), ledger);

        await Assert.That(ledger.GroupSettledAt[GroupA]).IsEqualTo(T2);
        await Assert.That(ledger.HouseholdSettledAt).IsEqualTo(T2);
    }

    [Test]
    public async Task Partial_settlement_does_not_mark_settled()
    {
        var ledger = EmptyLedger();
        _projection.Apply(ExpenseAt(T1, 1000, Alice, Bob), ledger);
        _projection.Apply(SettlementAt(T2, Bob, Alice, 200), ledger);

        await Assert.That(ledger.GroupSettledAt.ContainsKey(GroupA)).IsFalse();
        await Assert.That(ledger.HouseholdSettledAt).IsNull();
    }

    [Test]
    public async Task Household_settles_only_when_pair_sums_across_groups_net_to_zero()
    {
        var ledger = EmptyLedger();
        // GroupA: Bob owes Alice 500. GroupB: Alice owes Bob 300.
        _projection.Apply(ExpenseAt(T1, 1000, Alice, Bob, GroupA), ledger);
        _projection.Apply(ExpenseAt(T1, 600, Bob, Alice, GroupB), ledger);

        _projection.Apply(SettlementAt(T2, Bob, Alice, 500, GroupA), ledger);
        await Assert.That(ledger.GroupSettledAt[GroupA]).IsEqualTo(T2);
        await Assert.That(ledger.HouseholdSettledAt).IsNull();

        _projection.Apply(SettlementAt(T3, Alice, Bob, 300, GroupB), ledger);
        await Assert.That(ledger.GroupSettledAt[GroupB]).IsEqualTo(T3);
        await Assert.That(ledger.HouseholdSettledAt).IsEqualTo(T3);
    }

    [Test]
    public async Task Opposite_group_balances_netting_to_zero_settle_the_household_but_not_the_groups()
    {
        var ledger = EmptyLedger();
        _projection.Apply(ExpenseAt(T1, 1000, Alice, Bob, GroupA), ledger);
        _projection.Apply(ExpenseAt(T2, 1000, Bob, Alice, GroupB), ledger);

        await Assert.That(ledger.GroupSettledAt.Count).IsEqualTo(0);
        await Assert.That(ledger.HouseholdSettledAt).IsEqualTo(T2);
    }

    [Test]
    public async Task A_momentary_zero_inside_one_settle_does_not_keep_its_marker()
    {
        var ledger = EmptyLedger();
        // GroupA: Bob owes Alice 500. GroupB: Alice owes Bob 300.
        _projection.Apply(ExpenseAt(T1, 1000, Alice, Bob, GroupA), ledger);
        _projection.Apply(ExpenseAt(T1, 600, Bob, Alice, GroupB), ledger);
        _projection.Apply(SettlementAt(T2, Bob, Alice, 100, GroupA), ledger);

        // Same timestamp: the first leg nets the household to zero, the second leg moves it off zero again.
        _projection.Apply(SettlementAt(T3, Bob, Alice, 100, GroupA), ledger);
        await Assert.That(ledger.HouseholdSettledAt).IsEqualTo(T3);
        _projection.Apply(SettlementAt(T3, Bob, Alice, 100, GroupA), ledger);

        await Assert.That(ledger.HouseholdSettledAt).IsNull();
    }

    [Test]
    public async Task A_later_expense_leaves_the_old_marker_in_place()
    {
        var ledger = EmptyLedger();
        _projection.Apply(ExpenseAt(T1, 1000, Alice, Bob), ledger);
        _projection.Apply(SettlementAt(T2, Bob, Alice, 500), ledger);
        _projection.Apply(ExpenseAt(T3, 400, Alice, Bob), ledger);

        await Assert.That(ledger.GroupSettledAt[GroupA]).IsEqualTo(T2);
        await Assert.That(ledger.HouseholdSettledAt).IsEqualTo(T2);
    }

    [Test]
    public async Task Expense_that_touches_no_pair_does_not_mark()
    {
        var ledger = EmptyLedger();
        _projection.Apply(new ExpenseRecorded(Guid.NewGuid(), T1, Guid.NewGuid(), HouseholdId, GroupA, "Solo", T1,
            [new FundingSource(Alice, 100)], [new Allocation(Alice, 100)]), ledger);

        await Assert.That(ledger.GroupSettledAt.Count).IsEqualTo(0);
        await Assert.That(ledger.HouseholdSettledAt).IsNull();
    }

    [Test]
    public async Task Voiding_the_only_expense_marks_the_group_settled()
    {
        var ledger = EmptyLedger();
        var e = ExpenseAt(T1, 1000, Alice, Bob);
        _projection.Apply(e, ledger);

        await _projection.Apply(new ExpenseVoided(Guid.NewGuid(), T2, e.ExpenseId, HouseholdId, null,
            e.FundingSources, e.Allocations, Alice, "Test", ExpenseGroupId: GroupA), ledger, NoSession);

        await Assert.That(ledger.GroupSettledAt[GroupA]).IsEqualTo(T2);
    }

    [Test]
    public async Task Markers_ignore_a_settle_that_is_older_than_the_current_marker()
    {
        var ledger = EmptyLedger();
        _projection.Apply(ExpenseAt(T2, 1000, Alice, Bob), ledger);
        _projection.Apply(SettlementAt(T3, Bob, Alice, 500), ledger);

        // A historical import lands late: an expense and its settlement both dated before the marker.
        _projection.Apply(ExpenseAt(T1.AddMinutes(-30), 400, Alice, Bob), ledger);
        _projection.Apply(SettlementAt(T1, Bob, Alice, 200), ledger);

        await Assert.That(ledger.GroupSettledAt[GroupA]).IsEqualTo(T3);
        await Assert.That(ledger.HouseholdSettledAt).IsEqualTo(T3);
    }

    [Test]
    public async Task Settling_one_pair_of_three_leaves_the_other_pair_and_keeps_markers_open()
    {
        var ledger = EmptyLedger();
        _projection.Apply(new ExpenseRecorded(Guid.NewGuid(), T1, Guid.NewGuid(), HouseholdId, GroupA, "Dinner", T1,
            [new FundingSource(Alice, 900)],
            [new Allocation(Alice, 300), new Allocation(Bob, 300), new Allocation(Carol, 300)]), ledger);

        _projection.Apply(SettlementAt(T2, Bob, Alice, 300), ledger);

        await Assert.That(PairCents(ledger, Alice, Bob)).IsEqualTo(0L);
        await Assert.That(PairCents(ledger, Alice, Carol)).IsEqualTo(300L);
        await Assert.That(ledger.GroupSettledAt.ContainsKey(GroupA)).IsFalse();
        await Assert.That(ledger.HouseholdSettledAt).IsNull();

        _projection.Apply(SettlementAt(T3, Carol, Alice, 300), ledger);

        await Assert.That(ledger.GroupSettledAt[GroupA]).IsEqualTo(T3);
        await Assert.That(ledger.HouseholdSettledAt).IsEqualTo(T3);
    }

    [Test]
    public async Task Markers_still_advance_to_a_later_settle()
    {
        var ledger = EmptyLedger();
        _projection.Apply(ExpenseAt(T1, 1000, Alice, Bob), ledger);
        _projection.Apply(SettlementAt(T2, Bob, Alice, 500), ledger);
        _projection.Apply(ExpenseAt(T2.AddMinutes(1), 1000, Alice, Bob), ledger);
        _projection.Apply(SettlementAt(T3, Bob, Alice, 500), ledger);

        await Assert.That(ledger.GroupSettledAt[GroupA]).IsEqualTo(T3);
        await Assert.That(ledger.HouseholdSettledAt).IsEqualTo(T3);
    }
}
