using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HouseholdApp.Web.Shared.Web;

public static class LastModifiedPageModelExtensions
{
    public static IActionResult? NotModifiedOr304(this PageModel page, DateTimeOffset lastModified)
    {
        var formatted = lastModified.UtcDateTime.ToString("R");

        if (page.Request.Headers.IfModifiedSince == formatted)
        {
            page.Response.Headers.CacheControl = "private, no-cache";
            page.Response.Headers.LastModified = formatted;
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
            page.Response.Headers.LastModified = formatted;
            return Task.CompletedTask;
        });
        return null;
    }
}
