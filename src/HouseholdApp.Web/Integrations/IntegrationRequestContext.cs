using System.Globalization;
using System.Security.Claims;
using HouseholdApp.Application.Modules.Integrations.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Time;

namespace HouseholdApp.Web.Integrations;

// The checks a household page gets from HouseholdPageModel, for token requests: scope, membership (404 when
// denied) and the household time zone. IntegrationCultureFilter has already set the token owner's language.
public sealed class IntegrationRequestContext(
    IHouseholdGuard guard,
    IHouseholdClock clock,
    RequestTimeZone requestZone,
    IntegrationResponses responses)
{
    public async Task<(IntakeContext? Context, IResult? Denied)> ResolveAsync(ClaimsPrincipal user, string scope, CancellationToken ct)
    {
        if (!user.HasClaim(AccessTokenDefaults.ScopeClaim, scope))
            return (null, responses.Error(StatusCodes.Status403Forbidden, "Integrations.Error.Scope"));

        var userId = Guid.Parse(user.FindFirstValue("app_uid")!);
        var householdId = Guid.Parse(user.FindFirstValue(AccessTokenDefaults.HouseholdClaim)!);
        if (!await guard.IsMemberAsync(householdId, userId, ct))
            return (null, responses.Error(StatusCodes.Status404NotFound, "Integrations.Error.NotFound"));

        requestZone.Zone = await clock.ZoneAsync(householdId, ct);

        return (new IntakeContext(
            householdId, userId,
            Guid.Parse(user.FindFirstValue(AccessTokenDefaults.TokenIdClaim)!),
            OptionalGuid(user, AccessTokenDefaults.DefaultListClaim),
            OptionalGuid(user, AccessTokenDefaults.DefaultGroupClaim),
            CultureInfo.CurrentCulture), null);
    }

    private static Guid? OptionalGuid(ClaimsPrincipal user, string claim) =>
        Guid.TryParse(user.FindFirstValue(claim), out var id) ? id : null;
}
