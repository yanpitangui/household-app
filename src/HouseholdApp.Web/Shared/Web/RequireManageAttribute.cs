namespace HouseholdApp.Web.Shared.Web;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireManageAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireManageRolesAttribute : Attribute { }
