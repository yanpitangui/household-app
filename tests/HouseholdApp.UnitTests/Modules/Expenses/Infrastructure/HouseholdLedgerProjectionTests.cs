using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

namespace HouseholdApp.UnitTests.Modules.Expenses.Infrastructure;

public sealed class HouseholdLedgerProjectionTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Carol = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly HouseholdLedgerProjection _projection = new();

    private static HouseholdLedger EmptyLedger() => new() { Id = HouseholdId };

    private static ExpenseRecorded Expense(
        IReadOnlyList<FundingSource> funding,
        IReadOnlyList<Allocation> allocations) =>
        new(Guid.NewGuid(), Now, Guid.NewGuid(), HouseholdId, Guid.NewGuid(),
            "Test", Now, funding, allocations);

    private static SettlementRecorded Settlement(Guid payer, Guid recipient, long cents) =>
        new(Guid.NewGuid(), Now, Guid.NewGuid(), HouseholdId, payer, recipient, cents, Now);

    private static long PairCents(HouseholdLedger ledger, Guid u1, Guid u2)
    {
        var lo = u1 < u2 ? u1 : u2;
        var hi = u1 < u2 ? u2 : u1;
        var sign = u1 < u2 ? 1 : -1;
        var entry = ledger.Pairs.FirstOrDefault(p => p.UserId1 == lo && p.UserId2 == hi);
        return entry is null ? 0 : sign * entry.Cents;
    }

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

        _projection.Apply(new ExpenseVoided(Guid.NewGuid(), Now, Guid.NewGuid(), HouseholdId, null, funding, allocations, Guid.NewGuid(), "Groceries"), ledger);

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

        _projection.Apply(new ExpenseVoided(Guid.NewGuid(), Now, Guid.NewGuid(), HouseholdId, null, funding, allocations, Guid.NewGuid(), "Groceries"), ledger);

        await Assert.That(PairCents(ledger, Alice, Bob)).IsEqualTo(300L);
    }
}
