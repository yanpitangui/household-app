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
        services.AddEventHandler<HouseholdCreated, HouseholdAuthorizationSyncHandler>();
        services.AddEventHandler<HouseholdMemberJoined, HouseholdAuthorizationSyncHandler>();
        services.AddEventHandler<HouseholdMemberRemoved, HouseholdAuthorizationSyncHandler>();
        services.AddEventHandler<HouseholdRoleChanged, HouseholdAuthorizationSyncHandler>();
        return services;
    }
}
