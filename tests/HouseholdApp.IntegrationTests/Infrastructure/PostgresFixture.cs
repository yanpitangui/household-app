using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace HouseholdApp.IntegrationTests.Infrastructure;

public sealed class PostgresFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
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
        DatabaseMigrator.Migrate(ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }
}
