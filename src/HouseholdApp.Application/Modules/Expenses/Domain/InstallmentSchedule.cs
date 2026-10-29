using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HouseholdApp.Application.Modules.Expenses.Domain;

/// <summary>A split in effect from <see cref="FromNumber"/> on; allocations cover installments FromNumber..Count.</summary>
public sealed record SplitSegment(int FromNumber, IReadOnlyList<Allocation> Allocations);

/// <summary>Ties an Expense to its Installment Plan. An Early Payoff covers Number..ThroughNumber.</summary>
public sealed record InstallmentLink(Guid PlanId, int Number, int Count, int? ThroughNumber = null)
{
    public bool IsPayoff => ThroughNumber is not null;
}

/// <summary>The fixed terms of an Installment Plan; the down payment, when present, is installment 0.</summary>
public sealed record InstallmentTerms(
    long TotalCents, int Count, long DownPaymentCents, DateOnly PurchaseDate, DateOnly FirstDate)
{
    public bool HasDownPayment => DownPaymentCents > 0;
    public int FirstNumber => FirstNumberFor(DownPaymentCents);

    /// <summary>The first installment number of a plan: 0 with a down payment, else 1.</summary>
    public static int FirstNumberFor(long downPaymentCents) => downPaymentCents > 0 ? 0 : 1;

    public IEnumerable<int> Numbers => Enumerable.Range(FirstNumber, Count - FirstNumber + 1);
    public long InstallmentsCents => TotalCents - DownPaymentCents;

    // DateOnly.AddMonths lands on the month's last day when the day is missing (31 Jan + 1 month = 28/29 Feb), and
    // every date is computed from the first one so a short month never shifts the ones after it.
    public DateOnly DueDate(int number) => number == 0 ? PurchaseDate : FirstDate.AddMonths(number - 1);

    /// <summary>
    /// The down payment for number 0; for the installments an equal split of what is left after it, with the
    /// leftover cents going to the first installments (333,34 + 333,33 + 333,33).
    /// </summary>
    public long Amount(int number) =>
        number == 0
            ? DownPaymentCents
            : InstallmentsCents / Count + (number <= InstallmentsCents % Count ? 1 : 0);

    public long AmountRange(int from, int through)
    {
        long sum = 0;
        for (var n = from; n <= through; n++) sum += Amount(n);
        return sum;
    }

    /// <summary>
    /// Each member's cents of installment <paramref name="number"/>: the difference between their cumulative shares
    /// through this installment and through the previous one, so each member's total over the segment is exact.
    /// </summary>
    public IReadOnlyList<Allocation> Allocations(IReadOnlyList<SplitSegment> segments, int number)
    {
        var segment = segments.Last(s => s.FromNumber <= number);
        var through = Cumulative(segment, number);
        var before = Cumulative(segment, number - 1);
        return through
            .Select(a => new Allocation(a.UserId, a.Cents - before.First(b => b.UserId == a.UserId).Cents))
            .Where(a => a.Cents > 0)
            .ToList();
    }

    /// <summary>
    /// Each member's cents over installments fromNumber..Count, ordered by user id so that callers break rounding
    /// ties the same way <see cref="InstallmentSchedule.Distribute"/> does.
    /// </summary>
    public IReadOnlyList<Allocation> RemainingShares(IReadOnlyList<SplitSegment> segments, int fromNumber)
    {
        if (fromNumber > Count) return [];
        return Enumerable.Range(fromNumber, Count - fromNumber + 1)
            .SelectMany(n => Allocations(segments, n))
            .GroupBy(a => a.UserId)
            .Select(g => new Allocation(g.Key, g.Sum(a => a.Cents)))
            .OrderBy(a => a.UserId)
            .ToList();
    }

    /// <summary>
    /// True when every member with a share gets at least one exact cent of the smallest installment (number 1 and
    /// up), which keeps every member's cumulative share from shrinking, so no installment gives anyone a negative
    /// share. The down payment's cumulative share starts from zero, so any down payment size works.
    /// </summary>
    public bool EveryShareCoversACent(SplitSegment segment)
    {
        var weightTotal = segment.Allocations.Sum(a => a.Cents);
        var smallest = InstallmentsCents / Count;
        return segment.Allocations.Where(a => a.Cents > 0)
            .All(a => (Int128)smallest * a.Cents >= weightTotal);
    }

    private IReadOnlyList<Allocation> Cumulative(SplitSegment segment, int through)
    {
        var target = through < segment.FromNumber ? 0 : AmountRange(segment.FromNumber, through);
        return InstallmentSchedule.Distribute(target, segment.Allocations);
    }
}

/// <summary>The pure helpers shared by every Installment Plan operation, in whole cents.</summary>
public static class InstallmentSchedule
{
    public const int MinCount = 2;
    public const int MaxCount = 48;

    private static readonly Guid IdNamespace = Guid.Parse("6f1c2a3e-8a47-4d0b-9a61-2f3a8c9e0b17");

    /// <summary>Splits <paramref name="cents"/> in proportion to the weights, whole cents by largest remainder.</summary>
    public static IReadOnlyList<Allocation> Distribute(long cents, IReadOnlyList<Allocation> weights)
    {
        var weightTotal = weights.Sum(w => w.Cents);
        var shares = weights
            .Select(w => (w.UserId, Exact: Int128.DivRem((Int128)cents * w.Cents, weightTotal)))
            .Select(s => (s.UserId, Cents: (long)s.Exact.Quotient, Remainder: (long)s.Exact.Remainder))
            .ToList();
        var leftover = cents - shares.Sum(s => s.Cents);
        var bonusRank = shares
            .OrderByDescending(s => s.Remainder).ThenBy(s => s.UserId)
            .Select((s, rank) => (s.UserId, rank))
            .ToDictionary(x => x.UserId, x => x.rank);
        return shares.Select(s => new Allocation(s.UserId, s.Cents + (bonusRank[s.UserId] < leftover ? 1 : 0))).ToList();
    }

    /// <summary>
    /// Stable Expense id for installment <paramref name="number"/> of a plan (UUID version 8, SHA-256 of the plan id and
    /// the number), so a rerun of the sweep finds the stream it already started.
    /// </summary>
    public static Guid InstallmentExpenseId(Guid planId, int number)
    {
        Span<byte> input = stackalloc byte[36];
        IdNamespace.TryWriteBytes(input[..16], bigEndian: true, out _);
        planId.TryWriteBytes(input[16..32], bigEndian: true, out _);
        BinaryPrimitives.WriteInt32BigEndian(input[32..], number);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash[..16], bigEndian: true);
    }
}
