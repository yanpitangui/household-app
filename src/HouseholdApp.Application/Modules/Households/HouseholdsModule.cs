using HouseholdApp.Application.Modules.Households.Application.Operations;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Modules.Households.Infrastructure;
using HouseholdApp.Application.Shared.Events;

namespace HouseholdApp.Application.Modules.Households;

public static class HouseholdsModule
{
    public static IServiceCollection AddHouseholdsModule(this IServiceCollection services)
    {
        services.AddScoped<IHouseholdRepository, HouseholdRepository>();
        services.AddScoped<IHouseholdCommands, HouseholdCommandService>();
        services.AddScoped<IHouseholdQueries, HouseholdQueryService>();
        services.Decorate<IHouseholdQueries, CachingHouseholdQueryService>();
        services.AddScoped<IHouseholdQueriesWithLastModified>(sp => (IHouseholdQueriesWithLastModified)sp.GetRequiredService<IHouseholdQueries>());
        services.AddTransactionalEventHandler<HouseholdCreated, HouseholdAuthorizationSyncHandler>();
        services.AddEventHandler<HouseholdCreated, HouseholdCacheInvalidationHandler>();
        services.AddTransactionalEventHandler<HouseholdMemberJoined, HouseholdAuthorizationSyncHandler>();
        services.AddEventHandler<HouseholdMemberJoined, HouseholdCacheInvalidationHandler>();
        services.AddTransactionalEventHandler<HouseholdMemberRemoved, HouseholdAuthorizationSyncHandler>();
        services.AddEventHandler<HouseholdMemberRemoved, HouseholdCacheInvalidationHandler>();
        services.AddTransactionalEventHandler<HouseholdRoleChanged, HouseholdAuthorizationSyncHandler>();
        services.AddEventHandler<HouseholdRoleChanged, HouseholdCacheInvalidationHandler>();
        return services;
    }
}
