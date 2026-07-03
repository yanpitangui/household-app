using System.Collections.Concurrent;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using JasperFx;
using JasperFx.Events.Projections;
using Marten;

namespace HouseholdApp.IntegrationTests.Infrastructure;

/// <summary>
/// Shared per-connection-string Marten store for the expenses schema. Test classes that create
/// their own DocumentStore.For(...) each trigger their own AutoCreate.All migration; two of them
/// racing against the same live schema under parallel test execution causes DDL "already exists"
/// failures. Sharing one Lazy&lt;IDocumentStore&gt; across all Expense integration tests avoids that.
/// </summary>
public static class ExpenseDocumentStore
{
    private static readonly ConcurrentDictionary<string, Lazy<IDocumentStore>> Stores = new();

    public static IDocumentStore For(string connectionString) => Stores
        .GetOrAdd(connectionString, cs => new Lazy<IDocumentStore>(() => DocumentStore.For(opts =>
        {
            opts.Connection(cs);
            opts.DatabaseSchemaName = "expenses";
            opts.Events.AddEventType<ExpenseRecorded>();
            opts.Events.AddEventType<ExpenseVoided>();
            opts.Events.AddEventType<SettlementRecorded>();
            opts.Projections.Add<ExpenseReadModelProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<HouseholdLedgerProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<ActivityEntryProjection>(ProjectionLifecycle.Inline);
            opts.AutoCreateSchemaObjects = AutoCreate.All;
        })))
        .Value;
}
