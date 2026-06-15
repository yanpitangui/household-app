using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HouseholdApp.Web.Pages;

[Authorize]
public class SettingsModel : PageModel
{
    public string CurrentCulture { get; private set; } = "en";

    public void OnGet()
    {
        CurrentCulture = System.Globalization.CultureInfo.CurrentUICulture.Name;
    }
}
