using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Pages.Expenses;
using HouseholdApp.Web.Services;
using R3;
using StackExchange.Redis;

namespace HouseholdApp.Web.Expenses;

public static class ExpenseStreamEndpoints
{
    public static IEndpointRouteBuilder MapExpenseStream(this IEndpointRouteBuilder app)
    {
        app.MapGet("/h/{householdId:guid}/Expenses/stream",
            async (Guid householdId,
                   Guid? group,
                   ICurrentUser currentUser,
                   IHouseholdGuard guard,
                   IExpenseQueries expenseQueries,
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
                var redisChannel = RedisChannel.Literal($"expense:{householdId}");
                var subject = new Subject<Unit>();

                // Held by reference so the finally block unsubscribes only this stream and other tabs
                // on the same channel keep receiving updates.
                Action<RedisChannel, RedisValue> onMessage = (_, _) => subject.OnNext(Unit.Default);

                ctx.Response.Headers["Content-Type"] = "text/event-stream";
                ctx.Response.Headers["Cache-Control"] = "no-cache";
                ctx.Response.Headers["X-Accel-Buffering"] = "no";

                // The home renders group cards plus the consolidated timeline; a group page passes ?group= and renders that group's timeline.
                async Task<string> RenderAsync(CancellationToken innerCt)
                {
                    if (group is { } groupId)
                    {
                        var page = await expenseQueries.GetGroupPageAsync(householdId, groupId, currentUser.Id, innerCt);
                        if (page is null)
                            return await viewRender.RenderPartialAsync("~/Pages/Expenses/_GroupGone.cshtml", householdId);

                        var upcomingHtml = await RenderUpcomingAsync(groupId, innerCt);
                        var listVm = new ExpensesListViewModel(
                            householdId, groupId, currentUser.Id, ExpenseTimeline.Merge(page.Expenses, page.Settlements),
                            page.HasOlder);
                        var listHtml = await viewRender.RenderPartialAsync("~/Pages/Expenses/_ExpensesList.cshtml", listVm);
                        var groupBalanceHtml = await viewRender.RenderPartialAsync("~/Pages/Expenses/_BalanceCard.cshtml",
                            new BalanceCardViewModel(householdId, page.Balances, OutOfBand: true, groupId, page.Name));
                        var groupOlderHtml = await RenderOlderResetAsync(page.HasOlder, page.FirstOlderCursor);
                        return upcomingHtml + listHtml + groupBalanceHtml + groupOlderHtml;
                    }

                    var home = await expenseQueries.GetExpensesHomeAsync(householdId, currentUser.Id, innerCt);
                    var cardsHtml = await viewRender.RenderPartialAsync("~/Pages/Expenses/_GroupCards.cshtml",
                        new GroupCardsViewModel(householdId, home.Groups));
                    var balanceHtml = await viewRender.RenderPartialAsync("~/Pages/Expenses/_BalanceCard.cshtml",
                        new BalanceCardViewModel(householdId, home.Balances, OutOfBand: true));
                    var homeUpcomingHtml = await RenderUpcomingAsync(null, innerCt);
                    var homeListHtml = await viewRender.RenderPartialAsync("~/Pages/Expenses/_ExpensesList.cshtml",
                        new ExpensesListViewModel(
                            householdId, null, currentUser.Id,
                            ExpenseTimeline.Merge(home.Expenses, home.Settlements), home.HasOlder));
                    var homeOlderHtml = await RenderOlderResetAsync(home.HasOlder, home.FirstOlderCursor);
                    return cardsHtml + homeUpcomingHtml + homeListHtml + balanceHtml + homeOlderHtml;
                }

                async Task<string> RenderUpcomingAsync(Guid? groupId, CancellationToken innerCt) =>
                    await viewRender.RenderPartialAsync("~/Pages/Expenses/_UpcomingStrip.cshtml",
                        new UpcomingStripViewModel(householdId, currentUser.Id,
                            await expenseQueries.GetUpcomingAsync(householdId, groupId, currentUser.Id, innerCt)));

                // Every push re-renders Recent, so already-loaded older pages are cleared and the button restarts
                // from the fresh cursor; both live outside the SSE container and swap out of band.
                async Task<string> RenderOlderResetAsync(bool hasOlder, string? cursor) =>
                    "<div id=\"expenses-older\" hx-swap-oob=\"morph\"></div>"
                    + await viewRender.RenderPartialAsync("~/Pages/Expenses/_ShowOlderButton.cshtml",
                        new ShowOlderButtonViewModel(cursor, hasOlder, OobSwap: "morph"));

                try
                {
                    await sub.SubscribeAsync(redisChannel, onMessage);
                    await WriteSseAsync(ctx.Response, await RenderAsync(ct), ct);

                    using var _ = subject
                        .Debounce(TimeSpan.FromMilliseconds(10), timeProvider)
                        .SubscribeAwait(async (__, innerCt) =>
                        {
                            await WriteSseAsync(ctx.Response, await RenderAsync(innerCt), innerCt);
                        }, AwaitOperation.Sequential);

                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException) { }
                finally
                {
                    subject.OnCompleted();
                    await sub.UnsubscribeAsync(redisChannel, onMessage);
                }
            }).RequireAuthorization();

        return app;
    }

    private static async Task WriteSseAsync(HttpResponse response, string html, CancellationToken ct)
    {
        await response.WriteAsync("event: expenses-list\n", ct);
        foreach (var line in html.ReplaceLineEndings("\n").Split('\n'))
            await response.WriteAsync($"data: {line}\n", ct);
        await response.WriteAsync("\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
