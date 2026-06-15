using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HouseholdApp.Web.Pages;

[AllowAnonymous]
public class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            if (Request.Cookies.TryGetValue("last_household", out var lastId)
                && Guid.TryParse(lastId, out var householdId))
                return Redirect($"/h/{householdId}");

            return RedirectToPage("/Households/Index");
        }
        return Page();
    }
}
