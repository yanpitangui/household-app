using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Shared.Events;
using Marten;
using Marten.Services;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure;

public sealed class ExpenseEventPublishingListener(IEventBus bus) : DocumentSessionListenerBase
{
    // "Transactional" here only means "runs before Marten's SaveChangesAsync commits" — Marten owns its
    // own private connection, so unlike the Dapper IUnitOfWork path, an ITransactionalEventHandler
    // dispatched from here does NOT share the same DB transaction/connection as this Marten write.
    public override async Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
    {
        foreach (var stream in session.PendingChanges.Streams())
        foreach (var e in stream.Events)
            bus.Enqueue((IDomainEvent)e.Data);

        await bus.FlushTransactionalAsync(token);
    }

    public override async Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        await bus.FlushDeferredAsync(token);
    }
}
