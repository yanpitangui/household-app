using System.Globalization;
using System.Security.Claims;
using HouseholdApp.Application.Modules.Identity.Application.Ports;
using Microsoft.Extensions.Options;

namespace HouseholdApp.Web.Integrations;

// Runs first so every response — including errors — uses the token owner's language.
public sealed class IntegrationCultureFilter(
    IUserQuery users, IOptions<RequestLocalizationOptions> localization) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var options = localization.Value;
        var culture = options.DefaultRequestCulture.Culture;
        if (Guid.TryParse(http.User.FindFirstValue("app_uid"), out var userId)
            && (await users.GetByIdAsync(userId, http.RequestAborted))?.Locale is { } locale)
            culture = options.SupportedCultures?.FirstOrDefault(c => c.Name == locale) ?? culture;

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return await next(context);
    }
}
