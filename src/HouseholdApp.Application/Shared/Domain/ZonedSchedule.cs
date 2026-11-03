namespace HouseholdApp.Application.Shared.Domain;

/// <summary>
/// A schedule read in a time zone: occurrence n happens at <c>DateOf(n)</c> + <c>Time</c> on the zone's wall clock.
/// A wall-clock time skipped by a DST jump happens at the first valid minute after it; a repeated one at its first pass.
/// </summary>
public sealed class ZonedSchedule(RecurrenceSchedule schedule, TimeZoneInfo zone)
{
    public RecurrenceSchedule Schedule { get; } = schedule;
    public TimeZoneInfo Zone { get; } = zone;

    public DateTimeOffset At(int n) => AtDate(Schedule.DateOf(n));

    /// <summary>The instant the schedule's time of day falls on <paramref name="date"/> in this zone.</summary>
    public DateTimeOffset AtDate(DateOnly date) => ToInstant(date.ToDateTime(Schedule.Time));

    /// <summary>The calendar date of an instant on this zone's wall clock.</summary>
    public DateOnly DateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);

    public DateTimeOffset NextAfter(DateTimeOffset instant)
    {
        var n = LowerBoundIndex(instant);
        while (At(n) <= instant) n++;
        return At(n);
    }

    /// <summary>
    /// The occurrence after the one on <paramref name="instant"/>'s date in this zone. A cursor kept across a zone change
    /// (or a schedule moved to the household clock) can sit before that date's occurrence; stepping past the date keeps
    /// each date due once.
    /// </summary>
    public DateTimeOffset NextAfterDateOf(DateTimeOffset instant)
    {
        var onDate = AtDate(DateOf(instant));
        return NextAfter(onDate > instant ? onDate : instant);
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

    private DateTimeOffset ToInstant(DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(local))
        {
            // Spring-forward gap: step to the first minute that exists.
            do local = local.AddMinutes(1); while (Zone.IsInvalidTime(local));
        }
        var offset = Zone.IsAmbiguousTime(local) ? Zone.GetAmbiguousTimeOffsets(local).Max() : Zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    // An index whose predecessor is at or before the instant, so callers only step forward a few times. Measured on the
    // zone's calendar: an occurrence on an earlier local date is always earlier than an instant on a later one.
    private int LowerBoundIndex(DateTimeOffset instant)
    {
        var days = DateOf(instant).DayNumber - Schedule.FirstDate.DayNumber;
        if (days <= 1) return 0;
        return Schedule.Frequency switch
        {
            RecurrenceFrequency.Daily => Math.Max(0, days - 2),
            RecurrenceFrequency.Weekly => Math.Max(0, days / 7 - 1),
            RecurrenceFrequency.Biweekly => Math.Max(0, days / 14 - 1),
            RecurrenceFrequency.Monthly => days / 31,
            RecurrenceFrequency.Annually => days / 366,
            _ => throw new ArgumentOutOfRangeException(nameof(Schedule.Frequency), Schedule.Frequency, null)
        };
    }
}
