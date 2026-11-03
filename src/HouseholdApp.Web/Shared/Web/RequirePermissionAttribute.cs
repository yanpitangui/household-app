using HouseholdApp.Application.Shared.Authorization;

namespace HouseholdApp.Web.Shared.Web;

/// <summary>The handler runs only for users holding this household-wide permission; others get 404.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequirePermissionAttribute(HouseholdPermission permission) : Attribute
{
    public HouseholdPermission Permission { get; } = permission;
}
