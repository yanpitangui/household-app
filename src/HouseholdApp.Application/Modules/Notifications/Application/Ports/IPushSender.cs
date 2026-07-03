namespace HouseholdApp.Application.Modules.Notifications.Application.Ports;

public interface IPushSender
{
    // Sends a push notification to every subscribed device of the given user. `url` is opaque —
    // this port never interprets it; the caller (whichever module raises the notification) owns
    // building it from its own routes.
    Task SendAsync(Guid userId, string title, string body, string url, CancellationToken ct = default);
}
