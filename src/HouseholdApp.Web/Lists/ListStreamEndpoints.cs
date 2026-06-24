using HouseholdApp.Application.Modules.Lists.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Services;
using R3;
using StackExchange.Redis;

namespace HouseholdApp.Web.Lists;

public static class ListStreamEndpoints
{
    public static IEndpointRouteBuilder MapListStream(this IEndpointRouteBuilder app)
    {
        app.MapGet("/h/{householdId:guid}/Lists/{listId:guid}/stream",
            async (Guid householdId, Guid listId,
                   ICurrentUser currentUser,
                   IHouseholdGuard guard,
                   IListQueries listQueries,
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
                var redisChannel = RedisChannel.Literal($"list:{listId}");
                var subject = new Subject<Unit>();

                await sub.SubscribeAsync(redisChannel, (_, _) => subject.OnNext(Unit.Default));

                ctx.Response.Headers["Content-Type"] = "text/event-stream";
                ctx.Response.Headers["Cache-Control"] = "no-cache";
                ctx.Response.Headers["X-Accel-Buffering"] = "no";

                var list = await listQueries.GetAsync(listId, ct);
                if (list is not null)
                {
                    var html = await viewRender.RenderPartialAsync("~/Pages/Lists/_ItemsList.cshtml", list);
                    await WriteSseAsync(ctx.Response, html, ct);
                }

                using var _ = subject
                    .Debounce(TimeSpan.FromMilliseconds(10), timeProvider)
                    .SubscribeAwait(async (__, innerCt) =>
                    {
                        var current = await listQueries.GetAsync(listId, innerCt);
                        if (current is null) return;
                        var html = await viewRender.RenderPartialAsync("~/Pages/Lists/_ItemsList.cshtml", current);
                        await WriteSseAsync(ctx.Response, html, innerCt);
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
        await response.WriteAsync("event: items-list\n", ct);
        foreach (var line in html.ReplaceLineEndings("\n").Split('\n'))
            await response.WriteAsync($"data: {line}\n", ct);
        await response.WriteAsync("\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
