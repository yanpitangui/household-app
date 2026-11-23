using System.Security.Claims;
using System.Text.Encodings.Web;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;
using HouseholdApp.Web.Shared.Localization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace HouseholdApp.Web.Integrations;

public static class AccessTokenDefaults
{
    public const string Scheme = "ApiToken";
    public const string Policy = "Integrations";
    public const string RateLimitPolicy = "integrations";
    public const string HouseholdClaim = "household_id";
    public const string ScopeClaim = "scope";
    public const string TokenIdClaim = "token_id";
    public const string DefaultListClaim = "default_list_id";
    public const string DefaultGroupClaim = "default_group_id";
}

// Bearer access tokens for /api/integrations only. Challenges and forbids answer in JSON so API clients always get
// a readable message.
public sealed class AccessTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAccessTokenAuthenticator authenticator,
    IStringLocalizer<SharedResource> loc) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string BearerPrefix = "Bearer ";
    private AccessTokenStatus? _status;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (header is null || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var check = await authenticator.AuthenticateAsync(header[BearerPrefix.Length..].Trim(), Context.RequestAborted);
        _status = check.Status;
        if (check.Token is not { } token) return AuthenticateResult.Fail(check.Status.ToString());

        var claims = new List<Claim>
        {
            new("app_uid", token.UserId.ToString()),
            new(AccessTokenDefaults.HouseholdClaim, token.HouseholdId.ToString()),
            new(AccessTokenDefaults.TokenIdClaim, token.Id.ToString()),
        };
        claims.AddRange(token.Scopes.Select(s => new Claim(AccessTokenDefaults.ScopeClaim, s)));
        if (token.DefaultListId is { } listId) claims.Add(new(AccessTokenDefaults.DefaultListClaim, listId.ToString()));
        if (token.DefaultGroupId is { } groupId) claims.Add(new(AccessTokenDefaults.DefaultGroupClaim, groupId.ToString()));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var key = _status switch
        {
            null => "Integrations.Error.TokenMissing",
            AccessTokenStatus.Expired => "Integrations.Error.TokenExpired",
            _ => "Integrations.Error.TokenInvalid",
        };
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Response.WriteAsJsonAsync(new IntegrationError(loc[key].Value));
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Response.WriteAsJsonAsync(new IntegrationError(loc["Integrations.Error.Scope"].Value));
    }
}
