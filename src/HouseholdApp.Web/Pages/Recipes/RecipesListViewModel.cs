using HouseholdApp.Application.Modules.Recipes.Application.Ports;

namespace HouseholdApp.Web.Pages.Recipes;

public sealed record RecipesListViewModel(Guid HouseholdId, IReadOnlyList<RecipeSummary> Recipes);
