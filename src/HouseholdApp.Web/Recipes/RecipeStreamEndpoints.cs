using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Pages.Recipes;
using HouseholdApp.Web.Services;
using R3;
using StackExchange.Redis;

namespace HouseholdApp.Web.Recipes;

public static class RecipeStreamEndpoints
{
    public static IEndpointRouteBuilder MapRecipeStream(this IEndpointRouteBuilder app)
    {
        app.MapGet("/h/{householdId:guid}/Recipes/stream",
            async (Guid householdId,
                   ICurrentUser currentUser,
                   IHouseholdGuard guard,
                   IRecipeQueries recipeQueries,
                   IViewRenderService viewRender,
                   IConnectionMultiplexer redis,
                   TimeProvider timeProvider,
                   HttpContext ctx,
                   CancellationToken ct) =>
            {
                if (!await guard.IsMemberAsync(householdId, currentUser.Id, ct))
                {
                    ctx.Response.StatusCode = 403;
                    return;
                }

                var sub = redis.GetSubscriber();
                var redisChannel = RedisChannel.Literal($"recipe:{householdId}");
                var subject = new Subject<Unit>();

                await sub.SubscribeAsync(redisChannel, (_, _) => subject.OnNext(Unit.Default));

                ctx.Response.Headers["Content-Type"] = "text/event-stream";
                ctx.Response.Headers["Cache-Control"] = "no-cache";
                ctx.Response.Headers["X-Accel-Buffering"] = "no";

                async Task<string> RenderAsync(CancellationToken innerCt)
                {
                    var recipes = await recipeQueries.ListAsync(householdId, innerCt);
                    var vm = new RecipesListViewModel(householdId, recipes);
                    return await viewRender.RenderPartialAsync("~/Pages/Recipes/_RecipesList.cshtml", vm);
                }

                await WriteSseAsync(ctx.Response, await RenderAsync(ct), ct);

                using var _ = subject
                    .Debounce(TimeSpan.FromMilliseconds(10), timeProvider)
                    .SubscribeAwait(async (__, innerCt) =>
                    {
                        await WriteSseAsync(ctx.Response, await RenderAsync(innerCt), innerCt);
                    }, AwaitOperation.Sequential);

                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException) { }
                finally
                {
                    subject.OnCompleted();
                    await sub.UnsubscribeAsync(redisChannel);
                }
            }).RequireAuthorization();

        return app;
    }

    private static async Task WriteSseAsync(HttpResponse response, string html, CancellationToken ct)
    {
        await response.WriteAsync("event: recipes-list\n", ct);
        foreach (var line in html.ReplaceLineEndings("\n").Split('\n'))
            await response.WriteAsync($"data: {line}\n", ct);
        await response.WriteAsync("\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
