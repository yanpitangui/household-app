using HouseholdApp.Application.Modules.Catalog.Application.Operations;
using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using HouseholdApp.Application.Modules.Catalog.Domain;
using HouseholdApp.Application.Modules.Catalog.Infrastructure;
using HouseholdApp.Application.Shared.Events;
using Microsoft.Extensions.DependencyInjection;

namespace HouseholdApp.Application.Modules.Catalog;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        services.AddScoped<ICatalogQueries, CatalogQueryService>();
        services.Decorate<ICatalogQueries, CachingCatalogQueryService>();
        services.AddScoped<ICatalogCommands, CatalogCommandService>();
        services.AddEventHandler<CategoryAdded, CatalogCacheInvalidationHandler>();
        services.AddEventHandler<CategoryUpdated, CatalogCacheInvalidationHandler>();
        services.AddEventHandler<CategoryDeleted, CatalogCacheInvalidationHandler>();
        return services;
    }
}
