using HouseholdApp.Application.Shared.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HouseholdApp.Web.Shared.Web;

[Authorize]
public abstract class AuthenticatedPageModel(ICurrentUser currentUser) : PageModel
{
    public Guid CurrentUserId => currentUser.Id;
}
