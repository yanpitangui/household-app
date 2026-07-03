using System.Net;
using HouseholdApp.Application.Modules.Notifications.Application;
using HouseholdApp.Application.Modules.Notifications.Application.Ports;
using Microsoft.Extensions.Options;
using WebPush;

namespace HouseholdApp.Application.Modules.Notifications.Infrastructure;

public sealed class PushDeliveryException(HttpStatusCode statusCode) : Exception
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public interface IWebPushClientAdapter
{
    Task SendAsync(PushSubscriptionInfo subscription, string payloadJson, CancellationToken ct);
}

public sealed class WebPushClientAdapter(HttpClient httpClient, IOptions<PushOptions> options) : IWebPushClientAdapter
{
    private readonly WebPushClient _client = new(httpClient);

    public async Task SendAsync(PushSubscriptionInfo subscription, string payloadJson, CancellationToken ct)
    {
        var vapid = new VapidDetails(options.Value.Subject, options.Value.VapidPublicKey, options.Value.VapidPrivateKey);
        var pushSubscription = new PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth);

        try
        {
            await _client.SendNotificationAsync(pushSubscription, payloadJson, vapid, cancellationToken: ct);
        }
        catch (WebPushException ex)
        {
            throw new PushDeliveryException(ex.StatusCode);
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // Raw transport-level failures (HttpRequestException, timeouts, DNS errors) aren't
            // wrapped by the WebPush library. Translate them too so WebPushSender's per-subscription
            // isolation holds for network errors, not just protocol-level WebPushException.
            throw new PushDeliveryException(HttpStatusCode.ServiceUnavailable);
        }
    }
}
