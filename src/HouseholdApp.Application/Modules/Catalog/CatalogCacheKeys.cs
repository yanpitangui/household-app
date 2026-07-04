namespace HouseholdApp.Application.Modules.Catalog;

internal static class CatalogCacheKeys
{
    internal static string Categories(Guid householdId, string language) => $"catalog-categories:{householdId}:{language}";
    internal static string CategoryById(Guid categoryId) => $"catalog-category:{categoryId}";
}
