using Microsoft.Extensions.DependencyInjection;

namespace HouseholdApp.Application.Shared.Scheduler;

public static class ScheduledJobRegistration
{
    /// <summary>Runs <typeparamref name="TJob"/> on <paramref name="cron"/> (5-field, UTC). The job resolves in its own scope per run.</summary>
    public static IServiceCollection AddScheduledJob<TJob>(this IServiceCollection services, string name, string cron)
        where TJob : class, IScheduledJob
    {
        services.AddKeyedScoped<IScheduledJob, TJob>(name);
        services.AddSingleton(new ScheduledJobDefinition(name, cron));
        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TJob"/> as the recurring job <paramref name="name"/>: entities are scheduled under it with
    /// <see cref="IRecurringJobScheduler"/>, and each run resolves the job in its own scope.
    /// </summary>
    public static IServiceCollection AddRecurringJob<TJob>(this IServiceCollection services, string name)
        where TJob : class, IRecurringJob
    {
        services.AddKeyedScoped<IRecurringJob, TJob>(name);
        services.AddSingleton(new RecurringJobDefinition(name));
        return services;
    }
}

internal sealed record ScheduledJobDefinition(string Name, string Cron);

internal sealed record RecurringJobDefinition(string Name);
