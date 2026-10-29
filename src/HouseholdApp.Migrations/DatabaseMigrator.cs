using DbUp;
using Npgsql;
using System.Reflection;

namespace HouseholdApp.Migrations;

public static class DatabaseMigrator
{
    // Arbitrary app-wide key for pg_advisory_lock; only this migrator takes it.
    private const long LockKey = 0x486f757365_4462L;

    public static bool Migrate(string connectionString)
    {
        EnsureDatabase.For.PostgresqlDatabase(connectionString);

        // Held for the whole upgrade so web instances starting together apply each script exactly once.
        using var lockConnection = new NpgsqlConnection(connectionString);
        lockConnection.Open();
        using (var acquire = new NpgsqlCommand($"SELECT pg_advisory_lock({LockKey})", lockConnection))
            acquire.ExecuteNonQuery();

        try
        {
            var upgrader = DeployChanges.To
                .PostgresqlDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
                .WithTransactionPerScript()
                .LogToConsole()
                .Build();

            var result = upgrader.PerformUpgrade();
            return result.Successful;
        }
        finally
        {
            using var release = new NpgsqlCommand($"SELECT pg_advisory_unlock({LockKey})", lockConnection);
            release.ExecuteNonQuery();
        }
    }
}
