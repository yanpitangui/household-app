using HouseholdApp.Application.Shared.Events;
using Marten;
using Marten.Services;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure;

public sealed class ExpenseSessionFactory(IDocumentStore store, IEventBus bus) : ISessionFactory
{
    public IQuerySession QuerySession() => store.QuerySession();

    public IDocumentSession OpenSession() => store.LightweightSession(new SessionOptions
    {
        Listeners = { new ExpenseEventPublishingListener(bus) }
    });
}
