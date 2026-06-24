using HouseholdApp.Application.Modules.Lists.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Services;
using StackExchange.Redis;
using System.Threading.Channels;

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
                   HttpContext ctx,
                   CancellationToken ct) =>
            {
                if (!await guard.IsMemberAsync(householdId, currentUser.Id, ct))
                {
                    ctx.Response.StatusCode = 403;
                    return;
                }

                var signals = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
                {
                    FullMode = BoundedChannelFullMode.DropNewest,
                    SingleWriter = true,
                    SingleReader = true
                });
                var sub = redis.GetSubscriber();
                var redisChannel = RedisChannel.Literal($"list:{listId}");

                await sub.SubscribeAsync(redisChannel, (_, _) => signals.Writer.TryWrite(0));

                ctx.Response.Headers["Content-Type"] = "text/event-stream";
                ctx.Response.Headers["Cache-Control"] = "no-cache";
                ctx.Response.Headers["X-Accel-Buffering"] = "no";

                var list = await listQueries.GetAsync(listId, ct);
                if (list is not null)
                {
                    var html = await viewRender.RenderPartialAsync("~/Pages/Lists/_ItemsList.cshtml", list);
                    await WriteSseAsync(ctx.Response, html, ct);
                }

                try
                {
                    await foreach (var _ in signals.Reader.ReadAllAsync(ct))
                    {
                        await Task.Delay(200, ct);        // trailing debounce: wait for burst to settle
                        while (signals.Reader.TryRead(out var __)) { }  // drain any signals that arrived during the wait

                        list = await listQueries.GetAsync(listId, ct);
                        if (list is null) continue;
                        var html = await viewRender.RenderPartialAsync("~/Pages/Lists/_ItemsList.cshtml", list);
                        await WriteSseAsync(ctx.Response, html, ct);
                    }
                }
                catch (OperationCanceledException) { }
                finally
                {
                    await sub.UnsubscribeAsync(redisChannel);
                    signals.Writer.TryComplete();
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
