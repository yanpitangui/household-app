using HouseholdApp.Application.Modules.Identity.Application.Operations;
using HouseholdApp.Application.Modules.Identity.Application.Ports;
using HouseholdApp.Application.Modules.Identity.Domain;
using HouseholdApp.Application.Modules.Identity.Infrastructure;
using HouseholdApp.Application.Shared.Events;

namespace HouseholdApp.Application.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddScoped<UserRepository>();
        services.AddScoped<IUserQuery, UserRepository>();
        services.Decorate<IUserQuery, CachingUserQueryService>();
        services.AddScoped<IUserProvisioning, UserRepository>();
        services.AddEventHandler<UserProvisioned, UserCacheInvalidationHandler>();
        return services;
    }
}
