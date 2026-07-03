using System.Net;
using System.Text.Json;
using HouseholdApp.Application.Modules.Notifications.Application.Ports;

namespace HouseholdApp.Application.Modules.Notifications.Infrastructure;

public sealed class WebPushSender(IPushSubscriptionCommands subscriptions, IWebPushClientAdapter client) : IPushSender
{
    public async Task SendAsync(Guid userId, string title, string body, string url, CancellationToken ct = default)
    {
        var subs = await subscriptions.GetForUserAsync(userId, ct);
        var payload = JsonSerializer.Serialize(new { title, body, url });

        foreach (var sub in subs)
        {
            try
            {
                await client.SendAsync(sub, payload, ct);
            }
            catch (PushDeliveryException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                await subscriptions.DeleteByEndpointAsync(sub.Endpoint, ct);
            }
            catch (PushDeliveryException)
            {
                // Delivery failed for a reason unrelated to subscription validity (e.g. transient
                // server error) — leave the subscription in place and keep trying the rest.
            }
        }
    }
}
