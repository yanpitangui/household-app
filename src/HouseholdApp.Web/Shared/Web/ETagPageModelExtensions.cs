using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HouseholdApp.Web.Shared.Web;

public static class ETagPageModelExtensions
{
    public static IActionResult? NotModifiedOr304(this PageModel page, string etag)
    {
        var quoted = $"\"{etag}\"";

        if (page.Request.Headers.IfNoneMatch == quoted)
        {
            page.Response.Headers.CacheControl = "private, no-cache";
            page.Response.Headers.ETag = quoted;
            return page.StatusCode(StatusCodes.Status304NotModified);
        }

        // A 200 still renders _Layout.cshtml, whose antiforgery meta tag call
        // (GetAndStoreTokens) unconditionally overwrites Cache-Control/Pragma to
        // "no-cache, no-store" AFTER this handler runs. Deferring to OnStarting
        // guarantees our values are written last, right before headers are sent.
        page.Response.OnStarting(() =>
        {
            page.Response.Headers.CacheControl = "private, no-cache";
            page.Response.Headers.Remove("Pragma");
            page.Response.Headers.ETag = quoted;
            return Task.CompletedTask;
        });
        return null;
    }
}
