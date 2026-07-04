using HouseholdApp.Application.Modules.Catalog.Application.Ports;
using HouseholdApp.Application.Modules.Recipes.Application.Ports;

namespace HouseholdApp.Application.Modules.Recipes.Application.Operations;

public sealed class RecipeListImportService(
    IRecipeQueries recipeQueries,
    ICatalogQueries catalogQueries) : IRecipeListImport
{
    public async Task<IReadOnlyList<ProposedListItem>> ProposeListItemsAsync(
        Guid householdId, Guid recipeId, CancellationToken ct = default)
    {
        var recipe = await recipeQueries.GetAsync(householdId, recipeId, ct);
        if (recipe is null) return [];

        // Parse qty/unit out of the name when not already structured
        var parsed = recipe.Ingredients.Select(i =>
        {
            if (i.Quantity is not null) return (i.Name, i.Quantity, i.Unit, SearchTerm: i.Name);
            var (qty, unit, name) = IngredientParser.Parse(i.Name);
            return (name, qty, unit, SearchTerm: name);
        }).ToList();

        var searchTerms = parsed.Select(p => p.SearchTerm).ToList();
        var matches = await catalogQueries.MatchIngredientsAsync(householdId, searchTerms, ct);

        return parsed.Select(p =>
        {
            matches.TryGetValue(p.SearchTerm, out var match);
            return new ProposedListItem(
                Name: match is not null ? match.Name : p.Item1,
                Quantity: p.Item2,
                Unit: p.Item3,
                CatalogItemId: match?.Id,
                CategoryId: match?.CategoryId,
                CategoryName: match?.CategoryName,
                CategoryEmoji: match?.CategoryEmoji,
                Matched: match is not null);
        }).ToList();
    }
}
