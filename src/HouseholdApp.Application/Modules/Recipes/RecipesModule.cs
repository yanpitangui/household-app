using HouseholdApp.Application.Modules.Recipes.Application.Operations;
using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using HouseholdApp.Application.Modules.Recipes.Domain;
using HouseholdApp.Application.Modules.Recipes.Infrastructure;
using HouseholdApp.Application.Shared.Events;

namespace HouseholdApp.Application.Modules.Recipes;

public static class RecipesModule
{
    public static IServiceCollection AddRecipesModule(this IServiceCollection services)
    {
        services.AddScoped<IRecipeRepository, RecipeRepository>();
        services.AddScoped<IRecipeCommands, RecipeCommandService>();
        services.AddScoped<IRecipeQueries, RecipeQueryService>();
        services.Decorate<IRecipeQueries, CachingRecipeQueryService>();
        services.AddScoped<IRecipeQueriesWithETag>(sp => (IRecipeQueriesWithETag)sp.GetRequiredService<IRecipeQueries>());
        services.AddScoped<IRecipeListImport, RecipeListImportService>();
        services.AddEventHandler<RecipeCreated, RecipeCacheInvalidationHandler>();
        services.AddEventHandler<RecipeDeleted, RecipeCacheInvalidationHandler>();
        return services;
    }
}
