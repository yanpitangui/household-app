using HouseholdApp.Application.Modules.Catalog.Application.Operations;
using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using HouseholdApp.Application.Modules.Catalog.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HouseholdApp.Application.Modules.Catalog;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        services.AddScoped<ICatalogQueries, CatalogQueryService>();
        services.AddScoped<ICatalogCommands, CatalogCommandService>();
        return services;
    }
}
