namespace HouseholdApp.Application.Modules.Notifications.Application;

public sealed class PushOptions
{
    public string VapidPublicKey { get; set; } = "";
    public string VapidPrivateKey { get; set; } = "";
    public string Subject { get; set; } = "";
}
