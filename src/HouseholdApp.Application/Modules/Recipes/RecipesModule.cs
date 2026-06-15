using HouseholdApp.Application.Modules.Recipes.Application.Operations;
using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using HouseholdApp.Application.Modules.Recipes.Infrastructure;

namespace HouseholdApp.Application.Modules.Recipes;

public static class RecipesModule
{
    public static IServiceCollection AddRecipesModule(this IServiceCollection services)
    {
        services.AddScoped<IRecipeRepository, RecipeRepository>();
        services.AddScoped<IRecipeCommands, RecipeCommandService>();
        services.AddScoped<IRecipeQueries, RecipeQueryService>();
        return services;
    }
}
