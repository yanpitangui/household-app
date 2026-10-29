using HouseholdApp.Application.Modules.Expenses.Domain;

namespace HouseholdApp.UnitTests.Modules.Expenses.Domain;

public sealed class InstallmentScheduleTests
{
    private static readonly Guid Ana = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bia = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Leo = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    private static readonly DateOnly Purchase = new(2026, 10, 17);
    private static readonly DateOnly First = new(2026, 11, 1);

    private static InstallmentTerms Terms(long total, int count, long down = 0, DateOnly? first = null) =>
        new(total, count, down, Purchase, first ?? First);

    [Test]
    [Arguments("2026-01-31", 2, "2026-02-28")]
    [Arguments("2028-01-31", 2, "2028-02-29")]
    [Arguments("2026-01-31", 3, "2026-03-31")]
    [Arguments("2026-11-10", 3, "2027-01-10")]
    [Arguments("2026-10-10", 1, "2026-10-10")]
    public async Task DueDate_keeps_the_day_and_clamps_short_months(string first, int number, string expected)
    {
        var due = Terms(1000, 12, first: DateOnly.Parse(first)).DueDate(number);
        await Assert.That(due).IsEqualTo(DateOnly.Parse(expected));
    }

    [Test]
    public async Task Amount_puts_leftover_cents_on_the_first_installments()
    {
        var terms = Terms(100000, 3);
        var amounts = terms.Numbers.Select(terms.Amount).ToList();
        await Assert.That(amounts).IsEquivalentTo(new long[] { 33334, 33333, 33333 });
    }

    [Test]
    public async Task RemainingShares_sums_each_member_over_the_tail_ordered_by_user_id()
    {
        var segments = new[] { new SplitSegment(1, new[] { new Allocation(Bia, 50), new Allocation(Ana, 50) }) };
        var terms = Terms(100000, 3);

        var tail = terms.RemainingShares(segments, 2);

        await Assert.That(tail.Select(a => a.UserId)).IsEquivalentTo(new[] { Ana, Bia });
        await Assert.That(tail.Sum(a => a.Cents)).IsEqualTo(66666L);
        await Assert.That(terms.RemainingShares(segments, 1).Sum(a => a.Cents)).IsEqualTo(100000L);
        await Assert.That(terms.RemainingShares(segments, 4)).IsEmpty();
    }

    [Test]
    public async Task AmountRange_sums_the_installments()
    {
        var terms = Terms(100000, 3);
        await Assert.That(terms.AmountRange(2, 3)).IsEqualTo(66666L);
        await Assert.That(terms.AmountRange(1, 3)).IsEqualTo(100000L);
    }

    [Test]
    public async Task Allocations_keep_each_member_total_exact_and_each_installment_balanced()
    {
        // R$ 1.000,00 in 3x, split 1/3 each: member totals 33334 / 33333 / 33333.
        var allocations = new[] { new Allocation(Ana, 33334), new Allocation(Bia, 33333), new Allocation(Leo, 33333) };
        var segments = new[] { new SplitSegment(1, allocations) };
        var terms = Terms(100000, 3);

        var rows = Enumerable.Range(1, 3).Select(n => terms.Allocations(segments, n)).ToList();

        for (var n = 1; n <= 3; n++)
            await Assert.That(rows[n - 1].Sum(a => a.Cents)).IsEqualTo(terms.Amount(n));
        foreach (var member in allocations)
            await Assert.That(rows.SelectMany(r => r).Where(a => a.UserId == member.UserId).Sum(a => a.Cents))
                .IsEqualTo(member.Cents);
    }

    [Test]
    public async Task Allocations_never_go_negative_across_random_valid_plans()
    {
        var random = new Random(42);
        for (var i = 0; i < 500; i++)
        {
            var count = random.Next(2, 49);
            var members = random.Next(1, 5);
            var total = random.NextInt64(count * 100L, 5_000_000);
            var weights = Enumerable.Range(0, members).Select(_ => random.NextInt64(1, 1000)).ToArray();
            var allocations = InstallmentSchedule.Distribute(total,
                weights.Select(w => new Allocation(Guid.NewGuid(), w)).ToList());
            var segment = new SplitSegment(1, allocations);
            var terms = Terms(total, count);
            if (!terms.EveryShareCoversACent(segment)) continue;

            for (var n = 1; n <= count; n++)
            {
                var row = terms.Allocations([segment], n);
                await Assert.That(row.Sum(a => a.Cents)).IsEqualTo(terms.Amount(n));

                var through = InstallmentSchedule.Distribute(terms.AmountRange(1, n), allocations);
                var before = InstallmentSchedule.Distribute(n == 1 ? 0 : terms.AmountRange(1, n - 1), allocations);
                foreach (var member in allocations)
                {
                    var delta = through.Single(a => a.UserId == member.UserId).Cents
                        - before.Single(a => a.UserId == member.UserId).Cents;
                    await Assert.That(delta >= 0).IsTrue();
                }
            }

            foreach (var member in allocations)
            {
                var memberTotal = Enumerable.Range(1, count)
                    .SelectMany(n => terms.Allocations([segment], n))
                    .Where(a => a.UserId == member.UserId).Sum(a => a.Cents);
                await Assert.That(memberTotal).IsEqualTo(member.Cents);
            }
        }
    }

    [Test]
    public async Task A_later_segment_applies_from_its_number_and_splits_only_the_remaining_amount()
    {
        // 4x R$ 100,00: 50/50 for 1-2, then Ana pays 100% of 3-4.
        var first = new SplitSegment(1, [new Allocation(Ana, 20000), new Allocation(Bia, 20000)]);
        var fromThree = new SplitSegment(3, [new Allocation(Ana, 20000)]);
        var segments = new[] { first, fromThree };
        var terms = Terms(40000, 4);

        var third = terms.Allocations(segments, 3);
        var installment2 = terms.Allocations(segments, 2);

        await Assert.That(third).IsEquivalentTo(new[] { new Allocation(Ana, 10000) });
        await Assert.That(installment2).IsEquivalentTo(new[] { new Allocation(Ana, 5000), new Allocation(Bia, 5000) });
    }

    [Test]
    public async Task Distribute_splits_proportionally_with_largest_remainder()
    {
        var shares = InstallmentSchedule.Distribute(85000,
            [new Allocation(Ana, 45000), new Allocation(Bia, 45000)]);
        await Assert.That(shares).IsEquivalentTo(new[] { new Allocation(Ana, 42500), new Allocation(Bia, 42500) });
    }

    [Test]
    public async Task EveryShareCoversACent_rejects_shares_below_one_cent_per_installment()
    {
        var tiny = new SplitSegment(1, [new Allocation(Ana, 2), new Allocation(Bia, 2), new Allocation(Leo, 1)]);
        await Assert.That(Terms(5, 5).EveryShareCoversACent(tiny)).IsFalse();

        var fine = new SplitSegment(1, [new Allocation(Ana, 75000), new Allocation(Bia, 75000)]);
        await Assert.That(Terms(150000, 5).EveryShareCoversACent(fine)).IsTrue();
    }

    [Test]
    public async Task InstallmentExpenseId_is_stable_and_distinct_per_number()
    {
        var plan = Guid.NewGuid();
        await Assert.That(InstallmentSchedule.InstallmentExpenseId(plan, 2))
            .IsEqualTo(InstallmentSchedule.InstallmentExpenseId(plan, 2));
        await Assert.That(InstallmentSchedule.InstallmentExpenseId(plan, 2))
            .IsNotEqualTo(InstallmentSchedule.InstallmentExpenseId(plan, 3));
        await Assert.That(InstallmentSchedule.InstallmentExpenseId(plan, 2))
            .IsNotEqualTo(InstallmentSchedule.InstallmentExpenseId(Guid.NewGuid(), 2));
    }

    [Test]
    public async Task Numbers_start_at_zero_only_with_a_down_payment()
    {
        await Assert.That(Terms(150000, 5).Numbers).IsEquivalentTo(new[] { 1, 2, 3, 4, 5 });
        await Assert.That(Terms(150000, 5, down: 50000).Numbers).IsEquivalentTo(new[] { 0, 1, 2, 3, 4, 5 });
        await Assert.That(Terms(150000, 5, down: 50000).FirstNumber).IsEqualTo(0);
        await Assert.That(Terms(150000, 5).FirstNumber).IsEqualTo(1);
    }

    [Test]
    public async Task Amount_with_a_down_payment_is_the_down_then_an_equal_split_of_the_rest()
    {
        var terms = Terms(150000, 5, down: 50000);
        await Assert.That(terms.Numbers.Select(terms.Amount))
            .IsEquivalentTo(new long[] { 50000, 20000, 20000, 20000, 20000, 20000 });
        await Assert.That(terms.AmountRange(0, 5)).IsEqualTo(150000L);

        // R$ 1.000,01 - R$ 500,00 = 500,01 over 3: the leftover cent lands on installment 1.
        var uneven = Terms(100001, 3, down: 50000);
        await Assert.That(uneven.Numbers.Select(uneven.Amount)).IsEquivalentTo(new long[] { 50000, 16667, 16667, 16667 });
    }

    [Test]
    public async Task DueDate_of_the_down_payment_is_the_purchase_date()
    {
        var terms = Terms(150000, 5, down: 50000);
        await Assert.That(terms.DueDate(0)).IsEqualTo(Purchase);
        await Assert.That(terms.DueDate(1)).IsEqualTo(First);
        await Assert.That(terms.DueDate(2)).IsEqualTo(First.AddMonths(1));
    }

    [Test]
    public async Task Allocations_with_a_down_payment_keep_each_member_total_exact_across_0_to_count()
    {
        var allocations = new[] { new Allocation(Ana, 50000), new Allocation(Bia, 50000), new Allocation(Leo, 50001) };
        var terms = Terms(150001, 5, down: 50001);
        var segments = new[] { new SplitSegment(0, allocations) };

        var rows = terms.Numbers.Select(n => terms.Allocations(segments, n)).ToList();

        for (var n = 0; n <= 5; n++)
            await Assert.That(rows[n].Sum(a => a.Cents)).IsEqualTo(terms.Amount(n));
        foreach (var member in allocations)
            await Assert.That(rows.SelectMany(r => r).Where(a => a.UserId == member.UserId).Sum(a => a.Cents))
                .IsEqualTo(member.Cents);
    }

    [Test]
    public async Task Allocations_with_a_down_payment_never_go_negative_across_random_valid_plans()
    {
        var random = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var count = random.Next(2, 49);
            var members = random.Next(1, 5);
            var total = random.NextInt64(count * 100L + 1, 5_000_000);
            var down = random.Next(0, 4) == 0 ? 1 : random.NextInt64(1, total - count + 1);
            var weights = Enumerable.Range(0, members).Select(_ => random.NextInt64(1, 1000)).ToArray();
            var allocations = InstallmentSchedule.Distribute(total,
                weights.Select(w => new Allocation(Guid.NewGuid(), w)).ToList());
            var terms = Terms(total, count, down);
            var segment = new SplitSegment(0, allocations);
            if (!terms.EveryShareCoversACent(segment)) continue;

            foreach (var n in terms.Numbers)
            {
                var row = terms.Allocations([segment], n);
                await Assert.That(row.Sum(a => a.Cents)).IsEqualTo(terms.Amount(n));
                await Assert.That(row.All(a => a.Cents >= 0)).IsTrue();
            }

            foreach (var member in allocations)
            {
                var memberTotal = terms.Numbers
                    .SelectMany(n => terms.Allocations([segment], n))
                    .Where(a => a.UserId == member.UserId).Sum(a => a.Cents);
                await Assert.That(memberTotal).IsEqualTo(member.Cents);
            }
        }
    }

    [Test]
    public async Task EveryShareCoversACent_checks_the_smallest_installment_not_the_down_payment()
    {
        var segment = new SplitSegment(0, [new Allocation(Ana, 2), new Allocation(Bia, 2), new Allocation(Leo, 1)]);

        // Same installments (5 over 5), a huge down payment on one of them: same answer.
        await Assert.That(Terms(100005, 5, down: 100000).EveryShareCoversACent(segment)).IsFalse();
        await Assert.That(Terms(5, 5).EveryShareCoversACent(segment)).IsFalse();

        var roomy = new SplitSegment(0, [new Allocation(Ana, 75000), new Allocation(Bia, 75000)]);
        await Assert.That(Terms(150000, 5, down: 1).EveryShareCoversACent(roomy)).IsTrue();
    }
}
