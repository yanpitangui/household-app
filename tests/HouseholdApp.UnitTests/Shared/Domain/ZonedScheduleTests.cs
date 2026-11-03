using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.UnitTests.Shared.Domain;

public sealed class ZonedScheduleTests
{
    private static readonly TimeOnly Nine = new(9, 0);
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static RecurrenceSchedule Schedule(RecurrenceFrequency f, int y, int m, int d) =>
        new(f, new DateOnly(y, m, d), Nine);

    private static DateTimeOffset At(int y, int m, int d, int h = 9) => new(y, m, d, h, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Utc(int y, int m, int d, int h, int min = 0) => new(y, m, d, h, min, 0, TimeSpan.Zero);

    [Test]
    public async Task NextAfter_is_strictly_after_the_instant()
    {
        var schedule = Schedule(RecurrenceFrequency.Weekly, 2026, 11, 1);

        await Assert.That(schedule.In(TimeZoneInfo.Utc).NextAfter(At(2026, 11, 1))).IsEqualTo(At(2026, 11, 8));
        await Assert.That(schedule.In(TimeZoneInfo.Utc).NextAfter(At(2026, 11, 1, 8))).IsEqualTo(At(2026, 11, 1));
    }

    [Test]
    public async Task NextAfter_before_the_first_date_is_the_first_occurrence()
    {
        var schedule = Schedule(RecurrenceFrequency.Monthly, 2026, 11, 10);

        await Assert.That(schedule.In(TimeZoneInfo.Utc).NextAfter(At(2020, 1, 1))).IsEqualTo(At(2026, 11, 10));
    }

    [Test]
    public async Task NextAfter_far_from_the_first_date_keeps_the_day_rule()
    {
        var schedule = Schedule(RecurrenceFrequency.Monthly, 2020, 1, 31);

        await Assert.That(schedule.In(TimeZoneInfo.Utc).NextAfter(At(2027, 2, 1))).IsEqualTo(At(2027, 2, 28));
    }

    [Test]
    public async Task Between_excludes_the_start_and_the_end()
    {
        var schedule = Schedule(RecurrenceFrequency.Weekly, 2026, 11, 1);

        var dates = schedule.In(TimeZoneInfo.Utc).Between(At(2026, 11, 1), At(2026, 11, 15)).ToList();

        await Assert.That(dates).IsEquivalentTo(new[] { At(2026, 11, 8) });
    }

    [Test]
    public async Task A_late_evening_occurrence_lands_on_the_next_utc_day_but_keeps_its_local_date()
    {
        var schedule = new RecurrenceSchedule(RecurrenceFrequency.Monthly, new DateOnly(2026, 10, 31), new TimeOnly(22, 0));
        var zoned = schedule.In(SaoPaulo);

        await Assert.That(zoned.At(0)).IsEqualTo(Utc(2026, 11, 1, 1));
        await Assert.That(zoned.DateOf(zoned.At(0))).IsEqualTo(new DateOnly(2026, 10, 31));
    }

    [Test]
    public async Task A_february_occurrence_falls_on_the_last_local_day()
    {
        var schedule = new RecurrenceSchedule(RecurrenceFrequency.Monthly, new DateOnly(2026, 10, 31), new TimeOnly(22, 0));
        var zoned = schedule.In(SaoPaulo);

        await Assert.That(zoned.At(4)).IsEqualTo(Utc(2027, 3, 1, 1));
        await Assert.That(zoned.DateOf(zoned.At(4))).IsEqualTo(new DateOnly(2027, 2, 28));
    }

    [Test]
    public async Task NextAfter_near_local_midnight_follows_the_local_date()
    {
        var schedule = new RecurrenceSchedule(RecurrenceFrequency.Daily, new DateOnly(2026, 11, 1), new TimeOnly(22, 0));
        var zoned = schedule.In(SaoPaulo);

        // 23:00 on 2 Nov in Sao Paulo is already 3 Nov in UTC.
        await Assert.That(zoned.NextAfter(Utc(2026, 11, 3, 2))).IsEqualTo(Utc(2026, 11, 4, 1));
        await Assert.That(zoned.NextAfter(Utc(2026, 11, 3, 0, 59))).IsEqualTo(Utc(2026, 11, 3, 1));
    }

    [Test]
    public async Task CountThrough_near_local_midnight_counts_local_occurrences()
    {
        var schedule = new RecurrenceSchedule(RecurrenceFrequency.Daily, new DateOnly(2026, 11, 1), new TimeOnly(22, 0));
        var zoned = schedule.In(SaoPaulo);

        await Assert.That(zoned.CountThrough(Utc(2026, 11, 3, 0, 59))).IsEqualTo(1);
        await Assert.That(zoned.CountThrough(Utc(2026, 11, 3, 1))).IsEqualTo(2);
        await Assert.That(zoned.CountThrough(Utc(2026, 11, 3, 2))).IsEqualTo(2);
    }

    [Test]
    public async Task A_time_skipped_by_the_spring_forward_jump_happens_at_the_first_valid_minute()
    {
        var schedule = new RecurrenceSchedule(RecurrenceFrequency.Daily, new DateOnly(2026, 3, 7), new TimeOnly(2, 30));
        var zoned = schedule.In(NewYork);

        await Assert.That(zoned.At(0)).IsEqualTo(Utc(2026, 3, 7, 7, 30));
        await Assert.That(zoned.At(1)).IsEqualTo(Utc(2026, 3, 8, 7));
        await Assert.That(zoned.At(2)).IsEqualTo(Utc(2026, 3, 9, 6, 30));
    }

    [Test]
    public async Task A_time_repeated_by_the_fall_back_jump_happens_at_its_first_pass()
    {
        var schedule = new RecurrenceSchedule(RecurrenceFrequency.Daily, new DateOnly(2026, 10, 31), new TimeOnly(1, 30));
        var zoned = schedule.In(NewYork);

        await Assert.That(zoned.At(1)).IsEqualTo(Utc(2026, 11, 1, 5, 30));
        await Assert.That(zoned.NextAfter(Utc(2026, 11, 1, 5, 30))).IsEqualTo(Utc(2026, 11, 2, 6, 30));
    }

    [Test]
    public async Task Occurrences_stay_in_order_and_complete_across_a_dst_change_far_from_the_first_date()
    {
        var schedule = new RecurrenceSchedule(RecurrenceFrequency.Daily, new DateOnly(2025, 1, 1), new TimeOnly(0, 30));
        var zoned = schedule.In(NewYork);

        var from = Utc(2026, 3, 5, 12);
        var found = zoned.Between(from, Utc(2026, 3, 12, 12)).ToList();

        await Assert.That(found.Count).IsEqualTo(7);
        await Assert.That(found.SequenceEqual(found.Order())).IsTrue();
        await Assert.That(zoned.CountThrough(from)).IsEqualTo(
            Enumerable.Range(0, 800).Count(n => zoned.At(n) <= from));
    }

    // Against a plain scan from occurrence 0, in zones on both sides of UTC and across DST, for month-end and leap-day
    // schedules, at instants on, just before and just after occurrences.
    [Test]
    [Arguments(RecurrenceFrequency.Daily)]
    [Arguments(RecurrenceFrequency.Weekly)]
    [Arguments(RecurrenceFrequency.Biweekly)]
    [Arguments(RecurrenceFrequency.Monthly)]
    [Arguments(RecurrenceFrequency.Annually)]
    public async Task Stepping_from_the_lower_bound_matches_a_scan_from_the_first_occurrence(RecurrenceFrequency frequency)
    {
        string[] zones = ["UTC", "America/Sao_Paulo", "America/New_York", "Pacific/Auckland", "Pacific/Kiritimati"];
        DateOnly[] firstDates = [new(2026, 1, 31), new(2028, 2, 29), new(2026, 3, 8)];
        TimeOnly[] times = [new(0, 30), new(2, 30), new(23, 30)];
        var horizon = frequency switch
        {
            RecurrenceFrequency.Daily => 120,
            RecurrenceFrequency.Annually => 9,
            _ => 40
        };
        var mismatches = new List<string>();

        foreach (var zoneId in zones)
        foreach (var first in firstDates)
        foreach (var time in times)
        {
            var zoned = new RecurrenceSchedule(frequency, first, time).In(TimeZoneInfo.FindSystemTimeZoneById(zoneId));
            var occurrences = Enumerable.Range(0, horizon + 2).Select(zoned.At).ToList();
            foreach (var probe in occurrences.Take(horizon).SelectMany(o =>
                         new[] { o, o.AddMinutes(-1), o.AddMinutes(1), o.AddHours(-13), o.AddHours(13) }))
            {
                var expected = occurrences.First(o => o > probe);
                var count = occurrences.Count(o => o <= probe);
                if (zoned.NextAfter(probe) != expected || zoned.CountThrough(probe) != count)
                    mismatches.Add($"{zoneId} {first} {time} @ {probe:O}");
            }
        }

        await Assert.That(mismatches).IsEmpty();
    }
}
