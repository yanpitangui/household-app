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
    private static readonly ConcurrentDictionary<(string, string), Lazy<IDocumentStore>> Stores = new();

    public static IDocumentStore For(string connectionString, string schema = "expenses") => Stores
        .GetOrAdd((connectionString, schema), key => new Lazy<IDocumentStore>(() => DocumentStore.For(opts =>
        {
            opts.Connection(key.Item1);
            opts.DatabaseSchemaName = key.Item2;
            opts.Events.AddEventType<ExpenseRecorded>();
            opts.Events.AddEventType<ExpenseVoided>();
            opts.Events.AddEventType<SettlementRecorded>();
            opts.Events.AddEventType<ExpenseGroupDeleted>();
            opts.Events.AddEventType<ExpenseGroupRestored>();
            opts.Events.AddEventType<ExpenseGroupUpdated>();
            opts.Events.AddEventType<InstallmentPlanCreated>();
            opts.Events.AddEventType<InstallmentPlanUpdated>();
            opts.Events.AddEventType<InstallmentPlanPaidEarly>();
            opts.Events.AddEventType<InstallmentPlanCancelled>();
            opts.Events.AddEventType<InstallmentPlanCompleted>();
            opts.Projections.Add<ExpenseReadModelProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<HouseholdLedgerProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<ActivityEntryProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<SettlementReadModelProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<InstallmentPlanProjection>(ProjectionLifecycle.Inline);
            opts.AutoCreateSchemaObjects = AutoCreate.All;
        })))
        .Value;
}
