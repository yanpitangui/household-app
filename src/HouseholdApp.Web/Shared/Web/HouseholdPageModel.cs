using System.Reflection;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HouseholdApp.Web.Shared.Web;

public abstract class HouseholdPageModel(ICurrentUser currentUser, IHouseholdGuard guard)
    : AuthenticatedPageModel(currentUser)
{
    public abstract Guid HouseholdId { get; set; }

    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext ctx, PageHandlerExecutionDelegate next)
    {
        var method = ctx.HandlerMethod?.MethodInfo;
        if (method == null)
        {
            await next();
            return;
        }

        bool allowed;
        if (method.GetCustomAttribute<RequireManageRolesAttribute>() != null)
            allowed = await guard.CanManageRolesAsync(HouseholdId, CurrentUserId);
        else if (method.GetCustomAttribute<RequireManageAttribute>() != null)
            allowed = await guard.CanManageAsync(HouseholdId, CurrentUserId);
        else
            allowed = await guard.IsMemberAsync(HouseholdId, CurrentUserId);

        if (!allowed)
        {
            ctx.Result = new NotFoundResult();
            return;
        }
        await next();
    }

    public override Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;
}
