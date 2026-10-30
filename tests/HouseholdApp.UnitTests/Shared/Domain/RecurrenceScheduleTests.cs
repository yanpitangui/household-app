using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.UnitTests.Shared.Domain;

public sealed class RecurrenceScheduleTests
{
    private static readonly TimeOnly Nine = new(9, 0);

    private static RecurrenceSchedule Schedule(RecurrenceFrequency f, int y, int m, int d) =>
        new(f, new DateOnly(y, m, d), Nine);

    private static DateTimeOffset At(int y, int m, int d, int h = 9) => new(y, m, d, h, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(RecurrenceFrequency.Daily, 2, 2026, 11, 3)]
    [Arguments(RecurrenceFrequency.Weekly, 2, 2026, 11, 15)]
    [Arguments(RecurrenceFrequency.Biweekly, 2, 2026, 11, 29)]
    [Arguments(RecurrenceFrequency.Monthly, 2, 2027, 1, 1)]
    [Arguments(RecurrenceFrequency.Annually, 2, 2028, 11, 1)]
    public async Task DateOf_steps_by_frequency(RecurrenceFrequency f, int n, int y, int m, int d)
    {
        var schedule = Schedule(f, 2026, 11, 1);

        await Assert.That(schedule.DateOf(n)).IsEqualTo(new DateOnly(y, m, d));
    }

    [Test]
    public async Task Monthly_on_the_31st_uses_the_last_day_and_does_not_drift()
    {
        var schedule = Schedule(RecurrenceFrequency.Monthly, 2026, 10, 31);

        var dates = Enumerable.Range(0, 5).Select(schedule.DateOf).ToList();

        await Assert.That(dates).IsEquivalentTo(new[]
        {
            new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 31),
            new DateOnly(2027, 1, 31), new DateOnly(2027, 2, 28)
        });
    }

    [Test]
    public async Task Annually_on_29_February_falls_on_the_28th_in_other_years()
    {
        var schedule = Schedule(RecurrenceFrequency.Annually, 2028, 2, 29);

        await Assert.That(schedule.DateOf(1)).IsEqualTo(new DateOnly(2029, 2, 28));
        await Assert.That(schedule.DateOf(4)).IsEqualTo(new DateOnly(2032, 2, 29));
    }

    [Test]
    public async Task NextAfter_is_strictly_after_the_instant()
    {
        var schedule = Schedule(RecurrenceFrequency.Weekly, 2026, 11, 1);

        await Assert.That(schedule.NextAfter(At(2026, 11, 1))).IsEqualTo(At(2026, 11, 8));
        await Assert.That(schedule.NextAfter(At(2026, 11, 1, 8))).IsEqualTo(At(2026, 11, 1));
    }

    [Test]
    public async Task NextAfter_before_the_first_date_is_the_first_occurrence()
    {
        var schedule = Schedule(RecurrenceFrequency.Monthly, 2026, 11, 10);

        await Assert.That(schedule.NextAfter(At(2020, 1, 1))).IsEqualTo(At(2026, 11, 10));
    }

    [Test]
    public async Task NextAfter_far_from_the_first_date_keeps_the_day_rule()
    {
        var schedule = Schedule(RecurrenceFrequency.Monthly, 2020, 1, 31);

        await Assert.That(schedule.NextAfter(At(2027, 2, 1))).IsEqualTo(At(2027, 2, 28));
    }

    [Test]
    public async Task Between_excludes_the_start_and_the_end()
    {
        var schedule = Schedule(RecurrenceFrequency.Weekly, 2026, 11, 1);

        var dates = schedule.Between(At(2026, 11, 1), At(2026, 11, 15)).ToList();

        await Assert.That(dates).IsEquivalentTo(new[] { At(2026, 11, 8) });
    }

    [Test]
    public async Task OccurrenceId_is_stable_and_differs_by_template_and_date()
    {
        var template = Guid.NewGuid();
        var date = new DateOnly(2026, 11, 1);

        await Assert.That(RecurrenceSchedule.OccurrenceId(template, date))
            .IsEqualTo(RecurrenceSchedule.OccurrenceId(template, date));
        await Assert.That(RecurrenceSchedule.OccurrenceId(template, date))
            .IsNotEqualTo(RecurrenceSchedule.OccurrenceId(template, date.AddDays(1)));
        await Assert.That(RecurrenceSchedule.OccurrenceId(template, date))
            .IsNotEqualTo(RecurrenceSchedule.OccurrenceId(Guid.NewGuid(), date));
    }
}
