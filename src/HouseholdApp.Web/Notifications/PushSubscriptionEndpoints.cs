using HouseholdApp.Application.Modules.Notifications.Application.Ports;
using HouseholdApp.Application.Shared.Identity;
using Microsoft.AspNetCore.Antiforgery;

namespace HouseholdApp.Web.Notifications;

public static class PushSubscriptionEndpoints
{
    public sealed record SubscribeRequest(string Endpoint, string P256dh, string Auth);
    public sealed record UnsubscribeRequest(string Endpoint);

    public static IEndpointRouteBuilder MapPushSubscriptions(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/push/subscribe",
            async (SubscribeRequest request, HttpContext ctx, IAntiforgery antiforgery,
                   ICurrentUser currentUser, IPushSubscriptionCommands commands, CancellationToken ct) =>
            {
                await antiforgery.ValidateRequestAsync(ctx);
                await commands.SubscribeAsync(currentUser.Id, request.Endpoint, request.P256dh, request.Auth, ct);
                return Results.NoContent();
            }).RequireAuthorization();

        app.MapPost("/api/push/unsubscribe",
            async (UnsubscribeRequest request, HttpContext ctx, IAntiforgery antiforgery,
                   ICurrentUser currentUser, IPushSubscriptionCommands commands, CancellationToken ct) =>
            {
                await antiforgery.ValidateRequestAsync(ctx);
                await commands.UnsubscribeAsync(currentUser.Id, request.Endpoint, ct);
                return Results.NoContent();
            }).RequireAuthorization();

        return app;
    }
}
