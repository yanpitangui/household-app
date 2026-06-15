using HouseholdApp.Application.Modules.Lists.Application.Operations;
using HouseholdApp.Application.Modules.Lists.Application.Ports;
using HouseholdApp.Application.Modules.Lists.Infrastructure;

namespace HouseholdApp.Application.Modules.Lists;

public static class ListsModule
{
    public static IServiceCollection AddListsModule(this IServiceCollection services)
    {
        services.AddScoped<IListRepository, ListRepository>();
        services.AddScoped<IListCommands, ListCommandService>();
        services.AddScoped<IListQueries, ListQueryService>();
        return services;
    }
}
