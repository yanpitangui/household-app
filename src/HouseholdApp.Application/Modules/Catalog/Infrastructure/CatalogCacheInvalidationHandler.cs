using HouseholdApp.Application.Modules.Catalog.Domain;
using HouseholdApp.Application.Shared.Events;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.Application.Modules.Catalog.Infrastructure;

internal sealed class CatalogCacheInvalidationHandler(IFusionCache cache)
    : IEventHandler<CategoryAdded>,
      IEventHandler<CategoryUpdated>,
      IEventHandler<CategoryDeleted>
{
    private static readonly string[] SupportedLanguages = ["en", "pt-BR"];

    public Task HandleAsync(CategoryAdded evt, CancellationToken ct = default) => InvalidateAsync(evt.HouseholdId, evt.CategoryId, ct);
    public Task HandleAsync(CategoryUpdated evt, CancellationToken ct = default) => InvalidateAsync(evt.HouseholdId, evt.CategoryId, ct);
    public Task HandleAsync(CategoryDeleted evt, CancellationToken ct = default) => InvalidateAsync(evt.HouseholdId, evt.CategoryId, ct);

    private async Task InvalidateAsync(Guid householdId, Guid categoryId, CancellationToken ct)
    {
        foreach (var language in SupportedLanguages)
            await cache.RemoveAsync(CatalogCacheKeys.Categories(householdId, language), token: ct);
        await cache.RemoveAsync(CatalogCacheKeys.CategoryById(categoryId), token: ct);
    }
}
