using HouseholdApp.Application.Modules.Tasks.Domain;
using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.UnitTests.Modules.Tasks.Domain;

public sealed class RecurringTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static RecurrenceSchedule Weekly(int d) =>
        new(RecurrenceFrequency.Weekly, new DateOnly(2026, 11, d), new TimeOnly(9, 0));

    private static RecurringTask Create(RecurrenceSchedule schedule) =>
        RecurringTask.Create(Guid.NewGuid(), "Water plants", null, null, schedule, Now);

    [Test]
    public async Task Create_rejects_a_first_date_before_today()
    {
        await Assert.That(() => Create(Weekly(9))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Create_today_with_the_time_passed_is_due_right_away()
    {
        var template = Create(Weekly(10));

        await Assert.That(template.SpawnDue(Now).Count).IsEqualTo(1);
    }

    [Test]
    public async Task SpawnDue_gives_each_task_its_own_due_date_and_advances()
    {
        var template = Create(Weekly(10));

        var tasks = template.SpawnDue(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));

        await Assert.That(tasks.Select(t => t.DueDate!.Value.Day)).IsEquivalentTo(new[] { 10, 17 });
        await Assert.That(tasks.Select(t => t.Id)).IsEquivalentTo(new[]
        {
            RecurrenceSchedule.OccurrenceId(template.Id, new DateOnly(2026, 11, 10)),
            RecurrenceSchedule.OccurrenceId(template.Id, new DateOnly(2026, 11, 17))
        });
        await Assert.That(template.NextRunAt).IsEqualTo(new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task SpawnDue_returns_nothing_when_inactive()
    {
        var template = Create(Weekly(10));
        template.Deactivate();

        await Assert.That(template.SpawnDue(Now)).IsEmpty();
    }

    [Test]
    public async Task Rehydrate_roundtrips_the_schedule()
    {
        var id = Guid.NewGuid();
        var template = RecurringTask.Rehydrate(id, Guid.NewGuid(), "T", null, null, Weekly(10), true, Now);

        await Assert.That(template.Id).IsEqualTo(id);
        await Assert.That(template.Schedule).IsEqualTo(Weekly(10));
    }
}
