using System.Security.Claims;

namespace HouseholdApp.Web.Shared.Web;

public static class ClaimsPrincipalExtensions
{
    public static string GetSubject(this ClaimsPrincipal principal) =>
        principal.FindFirst("sub")?.Value ?? throw new InvalidOperationException("Missing 'sub' claim.");

    public static string? GetDisplayName(this ClaimsPrincipal principal) =>
        principal.FindFirst("name")?.Value ?? principal.FindFirst("preferred_username")?.Value;

    public static string? GetPicture(this ClaimsPrincipal principal) =>
        principal.FindFirst("picture")?.Value is { Length: > 0 } p ? p : null;
}
