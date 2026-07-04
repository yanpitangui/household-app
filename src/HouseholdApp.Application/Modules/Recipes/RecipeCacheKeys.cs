namespace HouseholdApp.Application.Modules.Recipes;

internal static class RecipeCacheKeys
{
    internal static string List(Guid householdId) => $"recipe-list:{householdId}";
    internal static string Detail(Guid householdId, Guid recipeId) => $"recipe-detail:{householdId}:{recipeId}";
}
