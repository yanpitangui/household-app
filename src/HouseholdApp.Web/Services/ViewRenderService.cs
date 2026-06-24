using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace HouseholdApp.Web.Services;

public interface IViewRenderService
{
    // viewPath must be an absolute virtual path with extension, e.g. "~/Pages/Lists/_ItemsList.cshtml"
    Task<string> RenderPartialAsync<TModel>(string viewPath, TModel model);
}

internal sealed class ViewRenderService(
    IRazorViewEngine viewEngine,
    ITempDataProvider tempDataProvider,
    IHttpContextAccessor httpContextAccessor,
    IModelMetadataProvider metadataProvider) : IViewRenderService
{
    public async Task<string> RenderPartialAsync<TModel>(string viewPath, TModel model)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No active HttpContext.");

        var actionContext = new ActionContext(
            httpContext,
            httpContext.GetRouteData(),
            new ActionDescriptor());

        var viewResult = viewEngine.GetView(null, viewPath, isMainPage: false);

        if (!viewResult.Success)
            throw new InvalidOperationException(
                $"View not found: {viewPath}. Searched: {string.Join(", ", viewResult.SearchedLocations ?? [])}");

        await using var writer = new StringWriter();

        var viewData = new ViewDataDictionary<TModel>(
            metadataProvider,
            new ModelStateDictionary()) { Model = model };

        var tempData = new TempDataDictionary(httpContext, tempDataProvider);

        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewData,
            tempData,
            writer,
            new HtmlHelperOptions());

        await viewResult.View.RenderAsync(viewContext);
        return writer.ToString();
    }
}
