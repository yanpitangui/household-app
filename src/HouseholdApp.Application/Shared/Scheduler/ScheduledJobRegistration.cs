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
}

internal sealed record ScheduledJobDefinition(string Name, string Cron);
