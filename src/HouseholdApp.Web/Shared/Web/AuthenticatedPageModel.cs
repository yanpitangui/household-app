using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace HouseholdApp.Web.Shared.Web;

[Authorize]
public abstract class AuthenticatedPageModel(ICurrentUser currentUser) : PageModel
{
    public Guid CurrentUserId => currentUser.Id;

    [FromServices]
    public IStringLocalizer<SharedResource> Loc { get; set; } = default!;
}
