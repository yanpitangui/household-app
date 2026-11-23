using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Modules.Integrations.Application.Operations;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;
using HouseholdApp.Application.Modules.Integrations.Infrastructure;
using HouseholdApp.Application.Shared.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HouseholdApp.Application.Modules.Integrations;

public static class IntegrationsModule
{
    public static IServiceCollection AddIntegrationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IntegrationsOptions>(configuration.GetSection("Integrations"));
        services.AddScoped<IAccessTokenRepository, AccessTokenRepository>();
        services.Decorate<IAccessTokenRepository, CachingAccessTokenRepository>();
        services.AddSingleton(sp => new AccessTokenKey(sp.GetRequiredService<IOptions<IntegrationsOptions>>().Value.DecodeTokenKey()));
        services.AddScoped<AccessTokenService>();
        services.AddScoped<IAccessTokenCommands>(sp => sp.GetRequiredService<AccessTokenService>());
        services.AddScoped<IAccessTokenAuthenticator>(sp => sp.GetRequiredService<AccessTokenService>());
        services.AddScoped<IIntegrationIntake, IntegrationIntakeService>();
        services.AddEventHandler<HouseholdMemberRemoved, RevokeAccessTokensOnMemberRemoved>();
        return services;
    }
}
