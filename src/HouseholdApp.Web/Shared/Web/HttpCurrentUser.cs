using HouseholdApp.Application.Shared.Identity;
using System.Security.Claims;

namespace HouseholdApp.Web.Shared.Web;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private Guid? _id;

    public Guid Id
    {
        get
        {
            if (_id.HasValue) return _id.Value;
            var uid = accessor.HttpContext?.User.FindFirst("app_uid")?.Value
                ?? throw new InvalidOperationException("Missing 'app_uid' claim.");
            _id = Guid.Parse(uid);
            return _id.Value;
        }
    }
}
