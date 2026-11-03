using Dapper;
using HouseholdApp.IntegrationTests.Infrastructure;

namespace HouseholdApp.IntegrationTests.Modules.Tasks;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class RecurringTaskLocalTimeMigrationTests(PostgresFixture db)
{
    private const string ScriptEnd = "WHERE h.id = r.household_id;";

    // The shipped script narrowed to one row, so the shared test database's other rows stay unlocked.
    private static string NarrowedScriptSql()
    {
        var assembly = typeof(HouseholdApp.Migrations.DatabaseMigrator).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("0009_recurring_tasks_local_time.sql"));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        var script = reader.ReadToEnd().TrimEnd();
        if (!script.EndsWith(ScriptEnd, StringComparison.Ordinal))
            throw new InvalidOperationException($"0009 is expected to end with \"{ScriptEnd}\" so it can be narrowed to one row.");
        return script[..^1] + " AND r.id = @recurringId;";
    }

    [Test]
    [Arguments("2026-11-05", "12:00:00", "2026-11-05", "09:00:00")]
    [Arguments("2026-11-05", "01:00:00", "2026-11-04", "22:00:00")]
    public async Task A_utc_schedule_becomes_the_same_instant_on_the_household_wall_clock(
        string firstDate, string runTime, string expectedDate, string expectedTime)
    {
        var householdId = Guid.NewGuid();
        var recurringId = Guid.NewGuid();
        var nextRunAt = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await conn.ExecuteAsync(
            "INSERT INTO households.households (id, name, time_zone) VALUES (@householdId, 'Casa', 'America/Sao_Paulo')",
            new { householdId }, tx);
        await conn.ExecuteAsync(
            """
            INSERT INTO tasks.recurring_tasks (id, household_id, title, frequency, first_date, run_time, is_active, next_run_at)
            VALUES (@recurringId, @householdId, 'Water plants', 'Weekly', @first, @time, true, @nextRunAt)
            """,
            new { recurringId, householdId, first = DateOnly.Parse(firstDate), time = TimeOnly.Parse(runTime), nextRunAt }, tx);

        await conn.ExecuteAsync(NarrowedScriptSql(), new { recurringId }, tx);

        var row = await conn.QuerySingleAsync<(DateOnly FirstDate, TimeOnly RunTime, DateTimeOffset NextRunAt)>(
            "SELECT first_date, run_time, next_run_at FROM tasks.recurring_tasks WHERE id = @recurringId", new { recurringId }, tx);
        await tx.RollbackAsync();

        await Assert.That(row.FirstDate).IsEqualTo(DateOnly.Parse(expectedDate));
        await Assert.That(row.RunTime).IsEqualTo(TimeOnly.Parse(expectedTime));
        await Assert.That(row.NextRunAt).IsEqualTo(nextRunAt);
    }
}
