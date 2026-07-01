using System.Data;
using HouseholdApp.Application.Shared.Events;
using Npgsql;

namespace HouseholdApp.Application.Shared.Persistence;

internal sealed class DapperUnitOfWork(NpgsqlDataSource dataSource, IEventBus eventBus) : IUnitOfWork, IAsyncDisposable
{
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;
    private bool _committed;

    public IDbTransaction? CurrentTransaction => _transaction;

    public async Task<IDbConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        if (_connection is not null) return _connection;
        _connection = await dataSource.OpenConnectionAsync(ct);
        return _connection;
    }

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is not null) return;
        _connection ??= await dataSource.OpenConnectionAsync(ct);
        _transaction = await _connection.BeginTransactionAsync(ct);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("No transaction started.");
        await eventBus.FlushTransactionalAsync(ct);
        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
        _committed = true;
        await eventBus.FlushDeferredAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            if (!_committed)
                await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
        }
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
