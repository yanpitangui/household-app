using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Jobs;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Scheduler;
using JasperFx;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;

namespace HouseholdApp.Application.Modules.Expenses;

public static class ExpensesModule
{
    public static IServiceCollection AddExpensesModule(this IServiceCollection services, string connectionString)
    {
        services.AddMarten(opts =>
        {
            opts.Connection(connectionString);
            opts.DatabaseSchemaName = "expenses";

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
            opts.Events.AddEventType<RecurringExpenseCreated>();
            opts.Events.AddEventType<RecurringExpenseUpdated>();
            opts.Events.AddEventType<RecurringExpenseOccurred>();
            opts.Events.AddEventType<RecurringExpenseEnded>();

            opts.Projections.Add<ExpenseReadModelProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<HouseholdLedgerProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<ActivityEntryProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<SettlementReadModelProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<InstallmentPlanProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<RecurringExpenseProjection>(ProjectionLifecycle.Inline);

            opts.Schema.For<ExpenseReadModel>().Index(x => x.InstallmentPlanId);
            opts.Schema.For<ActivityEntry>().Index(x => x.InstallmentPlanId);
            opts.Schema.For<InstallmentPlanDocument>().Index(x => x.HouseholdId).Index(x => x.Status);
            opts.Schema.For<ExpenseReadModel>().Index(x => x.RecurringExpenseId);
            opts.Schema.For<ActivityEntry>().Index(x => x.RecurringExpenseId);
            opts.Schema.For<RecurringExpenseDocument>().Index(x => x.HouseholdId).Index(x => x.NextOccurrenceAt);

            opts.AutoCreateSchemaObjects = AutoCreate.All;
        })
        .BuildSessionsWith<ExpenseSessionFactory>(ServiceLifetime.Scoped)
        // Schema objects are created at startup so concurrent first requests never race on lazy DDL.
        .ApplyAllDatabaseChangesOnStartup();

        services.AddHostedService<RecurringExpenseStreamMigration>();

        services.AddScoped<IExpenseCommands, ExpenseCommandService>();
        services.AddScoped<IInstallmentPlanCommands, InstallmentPlanCommandService>();
        services.AddScoped<IRecurringExpenseCommands, RecurringExpenseCommandService>();
        services.AddScoped<IExpenseImport, ExpenseImportService>();
        services.AddScoped<IExpenseQueries, ExpenseQueryService>();
        services.AddScoped<IActivityFeedQueries, ActivityFeedQueryService>();
        services.AddEventHandler<HouseholdCreated, DefaultExpenseGroupHandler>();
        services.AddEventHandler<ExpenseRecorded, ExpensePushNotificationHandler>();
        services.AddEventHandler<ExpenseVoided, ExpensePushNotificationHandler>();
        services.AddEventHandler<SettlementRecorded, ExpensePushNotificationHandler>();
        services.AddEventHandler<InstallmentPlanCreated, ExpensePushNotificationHandler>();
        services.AddEventHandler<InstallmentPlanPaidEarly, ExpensePushNotificationHandler>();
        services.AddEventHandler<InstallmentPlanCancelled, ExpensePushNotificationHandler>();
        services.AddEventHandler<RecurringExpenseCreated, ExpensePushNotificationHandler>();
        services.AddEventHandler<RecurringExpenseEnded, ExpensePushNotificationHandler>();

        services.AddScheduledJob<InstallmentPlanSweepJob>(InstallmentPlanSweepJob.Name, InstallmentPlanSweepJob.Cron);
        services.AddScheduledJob<RecurringExpenseSweepJob>(RecurringExpenseSweepJob.Name, RecurringExpenseSweepJob.Cron);

        return services;
    }
}
