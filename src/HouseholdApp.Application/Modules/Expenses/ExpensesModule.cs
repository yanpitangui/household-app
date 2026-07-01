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
using TickerQ.DependencyInjection;

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

            opts.Projections.Add<ExpenseReadModelProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<HouseholdLedgerProjection>(ProjectionLifecycle.Inline);

            opts.AutoCreateSchemaObjects = AutoCreate.All;
        }).UseLightweightSessions();

        services.AddScoped<IRecurringExpenseRepository, RecurringExpenseRepository>();
        services.AddScoped<IExpenseCommands, ExpenseCommandService>();
        services.AddScoped<IExpenseQueries, ExpenseQueryService>();
        services.AddScoped<IRecurringJobScheduler, TickerQJobScheduler>();
        services.AddEventHandler<HouseholdCreated, DefaultExpenseGroupHandler>();

        services.MapTicker<RecurringExpenseJobs, Guid>();

        return services;
    }
}
