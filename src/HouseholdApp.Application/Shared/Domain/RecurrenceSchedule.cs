using System.Security.Cryptography;

namespace HouseholdApp.Application.Shared.Domain;

/// <summary>
/// When a recurring template fires, as wall-clock dates and a time of day (no zone; see <see cref="ZonedSchedule"/>). Occurrence n is computed from <see cref="FirstDate"/>, never from the one
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

    /// <summary>Reads the schedule on a zone's wall clock; every instant computation lives there.</summary>
    public ZonedSchedule In(TimeZoneInfo zone) => new(this, zone);

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
}
