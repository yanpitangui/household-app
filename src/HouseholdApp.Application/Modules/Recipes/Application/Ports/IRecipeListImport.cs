namespace HouseholdApp.Application.Modules.Recipes.Application.Ports;

public sealed record ProposedListItem(
    string Name,
    string? Quantity,
    string? Unit,
    Guid? CatalogItemId,
    Guid? CategoryId,
    string? CategoryName,
    string? CategoryEmoji,
    bool Matched);

public interface IRecipeListImport
{
    Task<IReadOnlyList<ProposedListItem>> ProposeListItemsAsync(
        Guid householdId, Guid recipeId, CancellationToken ct = default);
}
