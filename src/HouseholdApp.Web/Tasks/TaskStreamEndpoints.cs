using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Pages.Tasks;
using HouseholdApp.Web.Services;
using R3;
using StackExchange.Redis;

namespace HouseholdApp.Web.Tasks;

public static class TaskStreamEndpoints
{
    public static IEndpointRouteBuilder MapTaskStream(this IEndpointRouteBuilder app)
    {
        app.MapGet("/h/{householdId:guid}/Tasks/stream",
            async (Guid householdId, bool showCompleted,
                   ICurrentUser currentUser,
                   IHouseholdGuard guard,
                   ITaskQueries taskQueries,
                   IHouseholdQueries householdQueries,
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
                var redisChannel = RedisChannel.Literal($"task:{householdId}");
                var subject = new Subject<Unit>();

                await sub.SubscribeAsync(redisChannel, (_, _) => subject.OnNext(Unit.Default));

                ctx.Response.Headers["Content-Type"] = "text/event-stream";
                ctx.Response.Headers["Cache-Control"] = "no-cache";
                ctx.Response.Headers["X-Accel-Buffering"] = "no";

                async Task<string> RenderAsync(CancellationToken innerCt)
                {
                    var tasksTask = taskQueries.ListAsync(householdId, showCompleted, innerCt);
                    var membersTask = householdQueries.GetMembersAsync(householdId, innerCt);
                    await Task.WhenAll(tasksTask, membersTask);
                    var memberNames = membersTask.Result.ToDictionary(m => m.UserId, m => m.DisplayName);
                    var vm = new TasksListViewModel(householdId, tasksTask.Result, memberNames, showCompleted);
                    return await viewRender.RenderPartialAsync("~/Pages/Tasks/_TasksList.cshtml", vm);
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
        await response.WriteAsync("event: tasks-list\n", ct);
        foreach (var line in html.ReplaceLineEndings("\n").Split('\n'))
            await response.WriteAsync($"data: {line}\n", ct);
        await response.WriteAsync("\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
