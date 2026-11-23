using System.Text.Json;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;
using HouseholdApp.Web.Shared.Localization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace HouseholdApp.Web.Integrations;

public static class IntegrationEndpoints
{
    public const string Prefix = "/api/integrations";

    // Input caps, checked before parsing: a spoken sentence or a short item list fits well within them.
    public const int MaxTextLength = 1000;
    public const int MaxItems = 50;
    public const int MaxItemNameLength = 200;
    public const int MaxFieldLength = 200;
    public const long MaxBodyBytes = 64 * 1024;

    public sealed record ListItemsBody(string? Text, IReadOnlyList<ListItemInput>? Items, string? List);

    public sealed record ExpenseBody(string? Text, string? Amount, string? Description, string? Group, string? Payer);

    public static bool IsIntegrationRequest(HttpContext http) => http.Request.Path.StartsWithSegments(Prefix);

    // Runs right after UseForwardedHeaders: a token sent over plain HTTP is refused before anything reads it.
    public static IApplicationBuilder UseIntegrationsHttpsOnly(this IApplicationBuilder app, IWebHostEnvironment env) =>
        app.Use(async (http, next) =>
        {
            if (!env.IsDevelopment() && !http.Request.IsHttps && IsIntegrationRequest(http))
            {
                var loc = http.RequestServices.GetRequiredService<IStringLocalizer<SharedResource>>();
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                await http.Response.WriteAsJsonAsync(new IntegrationError(loc["Integrations.Error.HttpsRequired"].Value));
                return;
            }
            await next(http);
        });

    // Runs after UseStatusCodePagesWithReExecute: API responses keep their own (JSON) bodies.
    public static IApplicationBuilder UseIntegrationsWithoutStatusPages(this IApplicationBuilder app) =>
        app.Use((http, next) =>
        {
            if (IsIntegrationRequest(http) && http.Features.Get<IStatusCodePagesFeature>() is { } pages)
                pages.Enabled = false;
            return next(http);
        });

    public static IEndpointRouteBuilder MapIntegrations(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Prefix)
            .RequireAuthorization(AccessTokenDefaults.Policy)
            .RequireRateLimiting(AccessTokenDefaults.RateLimitPolicy)
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes))
            .AddEndpointFilter<IntegrationCultureFilter>()
            .AddEndpointFilter<IntegrationExceptionFilter>();
        group.MapPost("/list-items", AddListItemsAsync);
        group.MapPost("/expenses", RecordExpenseAsync);
        return app;
    }

    private static async Task<IResult> AddListItemsAsync(
        HttpContext http, IntegrationRequestContext requests, IIntegrationIntake intake, IntegrationResponses responses, CancellationToken ct)
    {
        var (context, denied) = await requests.ResolveAsync(http.User, AccessTokenScopes.ListsAdd, ct);
        if (denied is not null) return denied;

        var body = await ReadBodyAsync<ListItemsBody>(http, ct);
        if (body is null) return responses.Error(StatusCodes.Status400BadRequest, "Integrations.Error.BadBody");
        if ((body.Text is null) == (body.Items is null)) return responses.Error(StatusCodes.Status400BadRequest, "Integrations.Error.TextOrItems");
        if (body.Items?.Any(i => i?.Name is null) == true) return responses.Error(StatusCodes.Status400BadRequest, "Integrations.Error.BadBody");
        if (IsTooLarge(body)) return responses.Error(StatusCodes.Status400BadRequest, "Integrations.Error.TooLarge");

        var result = await intake.AddListItemsAsync(context!, new ListInput(body.Text, body.Items, body.List), ct);
        return responses.ListItems(result);
    }

    private static async Task<IResult> RecordExpenseAsync(
        HttpContext http, IntegrationRequestContext requests, IIntegrationIntake intake, IntegrationResponses responses, CancellationToken ct)
    {
        var (context, denied) = await requests.ResolveAsync(http.User, AccessTokenScopes.ExpensesRecord, ct);
        if (denied is not null) return denied;

        var body = await ReadBodyAsync<ExpenseBody>(http, ct);
        if (body is null) return responses.Error(StatusCodes.Status400BadRequest, "Integrations.Error.BadBody");
        if (IsTooLarge(body)) return responses.Error(StatusCodes.Status400BadRequest, "Integrations.Error.TooLarge");

        var result = await intake.RecordExpenseAsync(context!,
            new ExpenseInput(body.Text, body.Amount, body.Description, body.Group, body.Payer), ct);
        return responses.Expense(result);
    }

    private static bool IsTooLarge(ListItemsBody body) =>
        body.Text?.Length > MaxTextLength
        || body.List?.Length > MaxFieldLength
        || body.Items?.Count > MaxItems
        || (body.Items?.Any(i =>
            i.Name.Length > MaxItemNameLength || i.Quantity?.Length > MaxFieldLength || i.Unit?.Length > MaxFieldLength) ?? false);

    private static bool IsTooLarge(ExpenseBody body) =>
        body.Text?.Length > MaxTextLength
        || body.Amount?.Length > MaxFieldLength
        || body.Description?.Length > MaxFieldLength
        || body.Group?.Length > MaxFieldLength
        || body.Payer?.Length > MaxFieldLength;

    private static async Task<T?> ReadBodyAsync<T>(HttpContext http, CancellationToken ct) where T : class
    {
        try
        {
            return await http.Request.ReadFromJsonAsync<T>(ct);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
