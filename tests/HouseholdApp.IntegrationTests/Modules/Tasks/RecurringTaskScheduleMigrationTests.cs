using Dapper;
using HouseholdApp.IntegrationTests.Infrastructure;
using Npgsql;

namespace HouseholdApp.IntegrationTests.Modules.Tasks;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class RecurringTaskScheduleMigrationTests(PostgresFixture db)
{
    private static string FunctionSql()
    {
        var assembly = typeof(HouseholdApp.Migrations.DatabaseMigrator).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("0007_recurring_tasks_schedule.sql"));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        var script = reader.ReadToEnd();
        var start = script.IndexOf("CREATE FUNCTION", StringComparison.Ordinal);
        var end = script.IndexOf("END $$;", StringComparison.Ordinal) + "END $$;".Length;
        return script[start..end];
    }

    private async Task<(string Frequency, DateOnly FirstDate, TimeSpan RunTime)> ConvertAsync(string cron)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await conn.ExecuteAsync(FunctionSql(), transaction: tx);
        var row = await conn.QuerySingleAsync<(string Frequency, DateOnly FirstDate, TimeSpan RunTime)>(
            "SELECT frequency, first_date, run_time FROM tasks.cron_to_schedule(@cron)", new { cron }, tx);
        await tx.RollbackAsync();
        return row;
    }

    [Test]
    [Arguments("0 9 * * *", "Daily")]
    [Arguments("30 7 * * 1", "Weekly")]
    [Arguments("0 9 15 * *", "Monthly")]
    [Arguments("0 9 25 12 *", "Annually")]
    public async Task A_picker_cron_converts(string cron, string frequency)
    {
        await Assert.That((await ConvertAsync(cron)).Frequency).IsEqualTo(frequency);
    }

    [Test]
    public async Task The_time_comes_from_the_cron()
    {
        await Assert.That((await ConvertAsync("30 7 * * 1")).RunTime).IsEqualTo(new TimeSpan(7, 30, 0));
    }

    [Test]
    public async Task A_leap_day_annual_cron_uses_the_next_leap_day()
    {
        var schedule = await ConvertAsync("0 9 29 2 *");

        await Assert.That(schedule.Frequency).IsEqualTo("Annually");
        await Assert.That(schedule.FirstDate.Month).IsEqualTo(2);
        await Assert.That(schedule.FirstDate.Day).IsEqualTo(29);
        await Assert.That(DateTime.IsLeapYear(schedule.FirstDate.Year)).IsTrue();
    }

    [Test]
    [Arguments("0 9 1,15 * *")]
    [Arguments("*/5 * * * *")]
    [Arguments("0 9 * * 1-5")]
    [Arguments("0 9 32 * *")]
    [Arguments("0 9 0 * *")]
    [Arguments("0 9 15 13 *")]
    public async Task A_cron_with_no_picker_equivalent_raises(string cron)
    {
        await Assert.That(async () => await ConvertAsync(cron)).Throws<PostgresException>();
    }

    private static string Script(string prefix)
    {
        var assembly = typeof(HouseholdApp.Migrations.DatabaseMigrator).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.Contains($".{prefix}_"));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }

    // Applies 0001-0006 on a fresh database and seeds rows in the old shape, so 0007 can run against them.
    private async Task<NpgsqlConnection> OldShapeDatabaseAsync(params (string Cron, bool Active)[] rows)
    {
        var name = $"tasks_migration_{Guid.NewGuid():N}";
        await using (var admin = await db.DataSource.OpenConnectionAsync())
            await new NpgsqlCommand($"CREATE DATABASE {name}", admin).ExecuteNonQueryAsync();

        var conn = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(db.ConnectionString) { Database = name, Pooling = false }.ConnectionString);
        await conn.OpenAsync();
        foreach (var prefix in new[] { "0001", "0002", "0003", "0004", "0005", "0006" })
            await new NpgsqlCommand(Script(prefix), conn).ExecuteNonQueryAsync();
        foreach (var (cron, active) in rows)
            await conn.ExecuteAsync(
                "INSERT INTO tasks.recurring_tasks (household_id, title, cron_expression, is_active, next_run_at) VALUES (@h, @cron, @cron, @active, now())",
                new { h = Guid.NewGuid(), cron, active });
        return conn;
    }

    private static async Task RunMigrationAsync(NpgsqlConnection conn)
    {
        await using var tx = await conn.BeginTransactionAsync();
        await new NpgsqlCommand(Script("0007"), conn, tx).ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    private sealed record Migrated(string Frequency, DateOnly FirstDate, TimeOnly RunTime, bool IsActive, DateTime NextRunAt);

    private static async Task<(DateOnly Today, DateTime Now)> DbClockAsync(NpgsqlConnection conn)
    {
        var now = await conn.ExecuteScalarAsync<DateTime>("SELECT now() AT TIME ZONE 'UTC'");
        return (DateOnly.FromDateTime(now), now);
    }

    private static async Task<Dictionary<string, Migrated>> ReadAsync(NpgsqlConnection conn)
    {
        var result = new Dictionary<string, Migrated>();
        await using var cmd = new NpgsqlCommand(
            "SELECT title, frequency, first_date, run_time, is_active, next_run_at AT TIME ZONE 'UTC' FROM tasks.recurring_tasks", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            result[r.GetString(0)] = new Migrated(
                r.GetString(1), r.GetFieldValue<DateOnly>(2), r.GetFieldValue<TimeOnly>(3), r.GetBoolean(4),
                r.IsDBNull(5) ? default : r.GetDateTime(5));
        return result;
    }

    // The script reads the database clock, so a run that straddles midnight UTC is repeated on a fresh database.
    private async Task<(DateOnly Today, DateTime Now, Dictionary<string, Migrated> Rows)> MigrateAsync(
        params (string Cron, bool Active)[] seed)
    {
        for (var attempt = 0; ; attempt++)
        {
            await using var conn = await OldShapeDatabaseAsync(seed);
            var before = await DbClockAsync(conn);
            await RunMigrationAsync(conn);
            var after = await DbClockAsync(conn);
            if (before.Today == after.Today || attempt == 2)
                return (before.Today, after.Now, await ReadAsync(conn));
        }
    }

    private static DateTime At(DateOnly date, int hour = 0) => date.ToDateTime(new TimeOnly(hour, 0));

    [Test]
    public async Task Active_rows_convert_and_land_strictly_after_now()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var tomorrow = today.AddDays(1);
        var futureCron = tomorrow.Month == today.Month
            ? $"0 9 {tomorrow.Day} * *"
            : $"0 9 {tomorrow.Day} {tomorrow.Month} *";
        var weekly = $"0 0 * * {(int)today.DayOfWeek}";
        var monthly = $"0 0 {today.Day} * *";

        var (_, now, rows) = await MigrateAsync(
            ("0 0 * * *", true), (weekly, true), (monthly, true), (futureCron, true));

        await Assert.That(rows.Count).IsEqualTo(4);
        await Assert.That(rows.Values.All(r => r.NextRunAt > now)).IsTrue();
        // The values are derived from the database's own date, so they hold when the test starts just before midnight.
        var dbToday = DateOnly.FromDateTime(now);
        if (dbToday != today) return;

        await Assert.That(rows["0 0 * * *"]).IsEqualTo(
            new Migrated("Daily", today, new TimeOnly(0, 0), true, At(today.AddDays(1))));
        await Assert.That(rows[weekly]).IsEqualTo(
            new Migrated("Weekly", today, new TimeOnly(0, 0), true, At(today.AddDays(7))));
        await Assert.That(rows[monthly]).IsEqualTo(
            new Migrated("Monthly", today, new TimeOnly(0, 0), true, At(today.AddMonths(1))));
        var future = rows[futureCron];
        await Assert.That(future.FirstDate).IsEqualTo(tomorrow);
        await Assert.That(future.NextRunAt).IsEqualTo(At(tomorrow, 9));
    }

    [Test]
    public async Task The_cron_column_is_gone_after_the_script()
    {
        await using var conn = await OldShapeDatabaseAsync(("0 9 * * *", true));
        await RunMigrationAsync(conn);

        var columns = await conn.QueryAsync<string>(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'tasks' AND table_name = 'recurring_tasks'");

        await Assert.That(columns).DoesNotContain("cron_expression");
        await Assert.That(columns).Contains("frequency");
    }

    [Test]
    public async Task An_inactive_row_with_an_unmappable_cron_gets_a_schedule_and_stays_inactive()
    {
        var (_, _, rows) = await MigrateAsync(("0 9 1,15 * *", false), ("0 9 * * *", true));

        await Assert.That(rows["0 9 1,15 * *"].IsActive).IsFalse();
        await Assert.That(rows["0 9 1,15 * *"].Frequency).IsEqualTo("Monthly");
        await Assert.That(rows.Count).IsEqualTo(2);
    }

    [Test]
    public async Task An_active_row_with_an_unmappable_cron_stops_the_script_and_leaves_the_table_as_it_was()
    {
        await using var conn = await OldShapeDatabaseAsync(("0 9 * * *", true), ("0 9 1,15 * *", true));

        await Assert.That(async () => await RunMigrationAsync(conn)).Throws<PostgresException>();

        var crons = await conn.QueryAsync<string>("SELECT cron_expression FROM tasks.recurring_tasks ORDER BY cron_expression");
        await Assert.That(crons.Count()).IsEqualTo(2);
    }
}
