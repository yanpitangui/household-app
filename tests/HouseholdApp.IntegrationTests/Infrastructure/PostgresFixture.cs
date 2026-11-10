using HouseholdApp.Application.Modules.Households;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;
using Valtuutus.Core.Configuration;
using Valtuutus.Data;
using Valtuutus.Data.Postgres;
using Valtuutus.Data.Postgres.Weasel;

namespace HouseholdApp.IntegrationTests.Infrastructure;

public sealed class PostgresFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        // Default max_connections=100 is too low: 9 shared-session test classes each open their
        // own connection pool (some via independent Marten/Valtuutus pools, not the shared
        // NpgsqlDataSource), and under TUnit's auto-parallelism that can exceed 100 concurrent
        // connections, causing intermittent "sorry, too many clients already" failures.
        .WithCommand("-c", "max_connections=300")
        .Build();

    public NpgsqlDataSource DataSource { get; private set; } = default!;

    public string ConnectionString { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        ConnectionString = _container.GetConnectionString();
        var builder = new NpgsqlDataSourceBuilder(ConnectionString);
        DataSource = builder.Build();

        PersistenceExtensions.RegisterTypeHandlers();
        if (!DatabaseMigrator.Migrate(ConnectionString))
            throw new InvalidOperationException("Database migration failed. Check DbUp output above for details.");

        // Valtuutus's Weasel package owns the authz tables, applied after DbUp as the web host does.
        var schemaStream = typeof(HouseholdsModule).Assembly
            .GetManifestResourceStream("HouseholdApp.Application.Shared.Authorization.schema.vtt")!;
        var services = new ServiceCollection();
        services.AddValtuutusCore(schemaStream);
        services.AddPostgres(_ => () => new NpgsqlConnection(ConnectionString), new ValtuutusPostgresOptions { Schema = "authz" })
            .AddPostgresWeasel();
        await using var provider = services.BuildServiceProvider();
        await provider.ApplyValtuutusSchemaAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }
}
