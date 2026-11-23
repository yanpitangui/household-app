using System.Diagnostics;
using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Web.Shared.Localization;
using Microsoft.Extensions.Localization;

namespace HouseholdApp.Web.Integrations;

// API clients get a broken business rule in words, or a generic message with a trace id for anything else;
// exception text stays in the server logs.
public sealed class IntegrationExceptionFilter(
    IStringLocalizer<SharedResource> loc, ILogger<IntegrationExceptionFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (BadHttpRequestException bad)
        {
            return bad.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? Results.Json(new IntegrationError(loc["Integrations.Error.TooLarge"].Value), statusCode: StatusCodes.Status413PayloadTooLarge)
                : Results.Json(new IntegrationError(loc["Integrations.Error.BadBody"].Value), statusCode: StatusCodes.Status400BadRequest);
        }
        catch (DomainRuleException rule)
        {
            return Results.Json(new IntegrationError(loc[rule.Key, rule.Args].Value), statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Integration request failed");
            return Results.Json(
                new IntegrationError(loc["Integrations.Error.Unexpected"].Value, Activity.Current?.TraceId.ToString()),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
