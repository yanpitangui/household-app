using HouseholdApp.Application.Modules.Tasks.Application.Operations;
using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using HouseholdApp.Application.Modules.Tasks.Infrastructure;
using HouseholdApp.Application.Modules.Tasks.Infrastructure.Jobs;
using HouseholdApp.Application.Shared.Scheduler;
using TickerQ.DependencyInjection;

namespace HouseholdApp.Application.Modules.Tasks;

public static class TasksModule
{
    public static IServiceCollection AddTasksModule(this IServiceCollection services)
    {
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<ITaskCommands, TaskCommandService>();
        services.AddScoped<ITaskQueries, TaskQueryService>();
        services.AddScoped<IRecurringJobScheduler, TickerQJobScheduler>();
        services.MapTicker<RecurringTaskJobs, Guid>();
        return services;
    }
}
