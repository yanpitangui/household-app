using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Quartz;

namespace HouseholdApp.Application.Shared.Scheduler;

public static class SchedulerModule
{
    /// <summary>Key of the scheduler's own <see cref="DbDataSource"/>.</summary>
    public const string DataSourceKey = "scheduler";

    /// <summary>
    /// Quartz on PostgreSQL (schema <c>quartz</c>), clustered: any number of app instances share the schedule and each
    /// trigger fires on exactly one of them. Weasel creates and migrates the tables at startup under an advisory lock,
    /// so nodes starting together take turns. Everything Quartz-specific stays in this folder.
    /// </summary>
    public static IServiceCollection AddScheduler(this IServiceCollection services, string connectionString)
    {
        services.AddKeyedSingleton<DbDataSource>(DataSourceKey, (_, _) =>
        {
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            builder.ConnectionStringBuilder.ApplicationName = "household-app-scheduler";
            // Every node checks in and polls for due triggers every few seconds; leaving those commands out of tracing
            // keeps them from burying the app's own spans. Job runs are traced through Quartz's own activity source.
            builder.ConfigureTracing(tracing => tracing
                .ConfigureCommandFilter(_ => false)
                .ConfigureBatchFilter(_ => false));
            return builder.Build();
        });

        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(scheduler => scheduler.InstanceName = "HouseholdApp");
            quartz.UsePersistentStore(store =>
            {
                store.UsePostgres(dataSource => dataSource.DataSourceServiceKey = DataSourceKey);
                store.ConfigureStore(options => options.TablePrefix = "quartz.qrtz_");
                store.UseClustering();
                store.UseWeaselForPostgres(weasel => weasel.AutoCreate = JasperFx.AutoCreate.CreateOrUpdate);
            });
        });
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        // Registered after Quartz's hosted service, so the scheduler is built before the jobs are written.
        services.AddHostedService<SchedulerStartup>();
        return services;
    }
}
