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

                await sub.SubscribeAsync(redisChannel, (_, _) => subject.OnNext(Unit.Default));

                ctx.Response.Headers["Content-Type"] = "text/event-stream";
                ctx.Response.Headers["Cache-Control"] = "no-cache";
                ctx.Response.Headers["X-Accel-Buffering"] = "no";

                async Task<string> RenderAsync(CancellationToken innerCt)
                {
                    var summary = await expenseQueries.GetExpensesSummaryAsync(householdId, ct: innerCt);
                    var listVm = new ExpensesListViewModel(householdId, currentUser.Id, summary.Expenses);
                    var listHtml = await viewRender.RenderPartialAsync("~/Pages/Expenses/_ExpensesList.cshtml", listVm);
                    var balanceHtml = await viewRender.RenderPartialAsync("~/Pages/Expenses/_BalanceCard.cshtml", summary.Balances);
                    return listHtml + balanceHtml;
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
        await response.WriteAsync("event: expenses-list\n", ct);
        foreach (var line in html.ReplaceLineEndings("\n").Split('\n'))
            await response.WriteAsync($"data: {line}\n", ct);
        await response.WriteAsync("\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
