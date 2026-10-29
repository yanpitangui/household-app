using Microsoft.Extensions.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.Utilities.Base;

namespace HouseholdApp.Application.Shared.Scheduler;

public static class ScheduledJobRegistration
{
    /// <summary>
    /// Runs <typeparamref name="TJob"/> on <paramref name="cron"/> (UTC). The job resolves in its own scope per run.
    /// Scheduler-specific wiring lives only here, so replacing the scheduler means rewriting this method.
    /// </summary>
    public static IServiceCollection AddScheduledJob<TJob>(this IServiceCollection services, string name, string cron)
        where TJob : class, IScheduledJob
    {
        services.AddKeyedScoped<IScheduledJob, TJob>(name);
        services.MapTicker(name, async (TickerFunctionContext _, IServiceProvider provider, CancellationToken ct) =>
        {
            await using var scope = provider.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredKeyedService<IScheduledJob>(name).RunAsync(ct);
        }).WithCron(cron);
        return services;
    }
}
