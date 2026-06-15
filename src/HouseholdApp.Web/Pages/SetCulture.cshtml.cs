using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HouseholdApp.Web.Pages;

[AllowAnonymous]
public class SetCultureModel : PageModel
{
    private static readonly HashSet<string> _supported = ["en", "pt-BR"];

    public IActionResult OnGet(string culture, string returnUrl = "/")
    {
        if (_supported.Contains(culture))
        {
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });
        }

        return LocalRedirect(returnUrl);
    }
}
