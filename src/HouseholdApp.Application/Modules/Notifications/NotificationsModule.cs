using HouseholdApp.Application.Modules.Notifications.Application;
using HouseholdApp.Application.Modules.Notifications.Application.Ports;
using HouseholdApp.Application.Modules.Notifications.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace HouseholdApp.Application.Modules.Notifications;

public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PushOptions>(configuration.GetSection("Push"));
        services.AddScoped<IPushSubscriptionCommands, PushSubscriptionRepository>();
        services.AddHttpClient<IWebPushClientAdapter, WebPushClientAdapter>();
        services.AddScoped<IPushSender, WebPushSender>();
        return services;
    }
}
