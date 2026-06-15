using HouseholdApp.Application.Modules.Identity.Application.Ports;
using HouseholdApp.Application.Modules.Identity.Infrastructure;

namespace HouseholdApp.Application.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddScoped<UserRepository>();
        services.AddScoped<IUserQuery, UserRepository>();
        services.AddScoped<IUserProvisioning, UserRepository>();
        return services;
    }
}
