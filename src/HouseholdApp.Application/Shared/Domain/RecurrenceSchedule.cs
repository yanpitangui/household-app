using System.Security.Cryptography;

namespace HouseholdApp.Application.Shared.Domain;

/// <summary>
/// When a recurring template fires, in UTC. Occurrence n is computed from <see cref="FirstDate"/>, never from the one
/// before it, so a shorter month cannot shift the months after it (31 Oct, 30 Nov, 31 Dec).
/// </summary>
public sealed record RecurrenceSchedule(RecurrenceFrequency Frequency, DateOnly FirstDate, TimeOnly Time)
{
    private static readonly Guid IdNamespace = new("0d3f4a52-6f0e-4e6b-9a51-2f8f0c7d1b6e");

    public DateOnly DateOf(int n) => Frequency switch
    {
        RecurrenceFrequency.Daily => FirstDate.AddDays(n),
        RecurrenceFrequency.Weekly => FirstDate.AddDays(7 * n),
        RecurrenceFrequency.Biweekly => FirstDate.AddDays(14 * n),
        RecurrenceFrequency.Monthly => FirstDate.AddMonths(n),
        RecurrenceFrequency.Annually => FirstDate.AddYears(n),
        _ => throw new ArgumentOutOfRangeException(nameof(Frequency), Frequency, null)
    };

    public DateTimeOffset At(int n) => new(DateOf(n), Time, TimeSpan.Zero);

    public DateTimeOffset NextAfter(DateTimeOffset instant)
    {
        var n = LowerBoundIndex(instant);
        while (At(n) <= instant) n++;
        return At(n);
    }

    /// <summary>Occurrences after <paramref name="from"/> and before <paramref name="to"/>.</summary>
    public IEnumerable<DateTimeOffset> Between(DateTimeOffset from, DateTimeOffset to)
    {
        var n = LowerBoundIndex(from);
        while (At(n) <= from) n++;
        for (; At(n) < to; n++) yield return At(n);
    }

    /// <summary>How many occurrences fall at or before <paramref name="instant"/>.</summary>
    public int CountThrough(DateTimeOffset instant)
    {
        var n = LowerBoundIndex(instant);
        while (At(n) <= instant) n++;
        return n;
    }

    /// <summary>
    /// Stable id of what a template creates on a date (UUID version 8, SHA-256 of the template id and the date), so a
    /// rerun finds what it already created and a template creates at most one item per date.
    /// </summary>
    public static Guid OccurrenceId(Guid templateId, DateOnly date)
    {
        Span<byte> input = stackalloc byte[36];
        IdNamespace.TryWriteBytes(input[..16], bigEndian: true, out _);
        templateId.TryWriteBytes(input[16..32], bigEndian: true, out _);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(input[32..], date.DayNumber);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash[..16], bigEndian: true);
    }

    // An index at or before the occurrence wanted, close enough that callers only step forward a few times.
    private int LowerBoundIndex(DateTimeOffset instant)
    {
        var days = DateOnly.FromDateTime(instant.UtcDateTime).DayNumber - FirstDate.DayNumber;
        if (days <= 0) return 0;
        return Frequency switch
        {
            RecurrenceFrequency.Daily => Math.Max(0, days - 1),
            RecurrenceFrequency.Weekly => Math.Max(0, days / 7 - 1),
            RecurrenceFrequency.Biweekly => Math.Max(0, days / 14 - 1),
            RecurrenceFrequency.Monthly => days / 31,
            RecurrenceFrequency.Annually => days / 366,
            _ => throw new ArgumentOutOfRangeException(nameof(Frequency), Frequency, null)
        };
    }
}
