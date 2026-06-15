using System.Data;

namespace HouseholdApp.Application.Shared.Persistence;

public interface IUnitOfWork
{
    IDbTransaction? CurrentTransaction { get; }
    Task<IDbConnection> GetConnectionAsync(CancellationToken ct = default);
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
}
