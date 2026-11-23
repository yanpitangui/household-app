using HouseholdApp.Application.Modules.Identity.Application.Ports;
using HouseholdApp.Web.Integrations;
using Microsoft.AspNetCore.Localization;

namespace HouseholdApp.Web.Shared.Web;

// Stores the culture a signed-in user reads the app in (identity.users.locale), so messages built
// outside their requests (push notifications) use the same language. A marker cookie remembers what
// this browser last stored, so a request costs a string compare and the database is written only when
// the user or their language changes; across devices the most recent change wins.
public sealed class UserLocaleSyncMiddleware(RequestDelegate next)
{
    internal const string CookieName = "locale_synced";

    public async Task InvokeAsync(HttpContext context, IUserLocales locales, TimeProvider time)
    {
        // The stored language follows the user's browser; token requests read it as the owner's language.
        if (context.User.Identity?.AuthenticationType == AccessTokenDefaults.Scheme)
        {
            await next(context);
            return;
        }

        if (context.User.FindFirst("app_uid")?.Value is { } uid && Guid.TryParse(uid, out var userId)
            && context.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture.Name is { Length: > 0 } culture)
        {
            var marker = $"{userId}.{culture}";
            if (context.Request.Cookies[CookieName] != marker)
            {
                await locales.SetLocaleAsync(userId, culture, context.RequestAborted);
                context.Response.Cookies.Append(CookieName, marker, new CookieOptions
                {
                    Expires = time.GetUtcNow().AddYears(1),
                    HttpOnly = true,
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = context.Request.IsHttps,
                });
            }
        }

        await next(context);
    }
}
