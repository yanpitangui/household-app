using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HouseholdApp.Web.Shared.Web;

public static class LastModifiedPageModelExtensions
{
    /// <summary>
    /// Answers 304 when the browser's copy is still current. The ETag combines the data's last change with the UI
    /// culture, so switching language yields a fresh page instead of the copy rendered in the previous language.
    /// </summary>
    public static IActionResult? NotModifiedOr304(this PageModel page, DateTimeOffset lastModified)
    {
        var etag = $"\"{lastModified.UtcTicks:x}-{CultureInfo.CurrentUICulture.Name}\"";

        if (page.Request.Headers.IfNoneMatch == etag)
        {
            page.Response.Headers.CacheControl = "private, no-cache";
            page.Response.Headers.ETag = etag;
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
            page.Response.Headers.ETag = etag;
            return Task.CompletedTask;
        });
        return null;
    }
}
