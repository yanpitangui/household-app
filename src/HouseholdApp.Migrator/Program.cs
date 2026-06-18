using HouseholdApp.Migrations;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__householdapp")
    ?? throw new InvalidOperationException("ConnectionStrings__householdapp environment variable is required");

var success = DatabaseMigrator.Migrate(connectionString);
return success ? 0 : 1;
