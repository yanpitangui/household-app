using System.Data;
using Npgsql;

namespace HouseholdApp.Application.Shared.Persistence;

internal sealed class DapperUnitOfWork(NpgsqlDataSource dataSource) : IUnitOfWork, IAsyncDisposable
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
        var conn = await dataSource.OpenConnectionAsync(ct);
        _connection ??= conn;
        _transaction = await _connection.BeginTransactionAsync(ct);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("No transaction started.");
        await _transaction.CommitAsync(ct);
        _committed = true;
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
