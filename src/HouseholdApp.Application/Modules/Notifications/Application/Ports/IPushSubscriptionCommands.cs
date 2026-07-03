namespace HouseholdApp.Application.Modules.Notifications.Application.Ports;

public sealed record PushSubscriptionInfo(Guid UserId, string Endpoint, string P256dh, string Auth);

public interface IPushSubscriptionCommands
{
    Task SubscribeAsync(Guid userId, string endpoint, string p256dh, string auth, CancellationToken ct = default);
    Task UnsubscribeAsync(Guid userId, string endpoint, CancellationToken ct = default);
    Task<IReadOnlyList<PushSubscriptionInfo>> GetForUserAsync(Guid userId, CancellationToken ct = default);
    Task DeleteByEndpointAsync(string endpoint, CancellationToken ct = default);
}
