using HouseholdApp.Application.Modules.Recipes.Domain;
using HouseholdApp.Application.Shared.Events;
using ZiggyCreatures.Caching.Fusion;

namespace HouseholdApp.Application.Modules.Recipes.Infrastructure;

internal sealed class RecipeCacheInvalidationHandler(IFusionCache cache)
    : IEventHandler<RecipeCreated>,
      IEventHandler<RecipeDeleted>
{
    public async Task HandleAsync(RecipeCreated evt, CancellationToken ct = default) =>
        await cache.RemoveAsync(RecipeCacheKeys.List(evt.HouseholdId), token: ct);

    public async Task HandleAsync(RecipeDeleted evt, CancellationToken ct = default)
    {
        await cache.RemoveAsync(RecipeCacheKeys.List(evt.HouseholdId), token: ct);
        await cache.RemoveAsync(RecipeCacheKeys.Detail(evt.RecipeId), token: ct);
    }
}
