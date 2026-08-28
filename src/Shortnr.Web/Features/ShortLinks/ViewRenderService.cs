using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace Shortnr.Web.Features.ShortLinks;

/// <summary>
/// Renders a Razor partial to a string so non-Razor-Pages surfaces (the minimal-API
/// redirect endpoint) can serve HTML that lives in a .cshtml file instead of being
/// built inline in C#. Used only for the pixel interstitial, which must be a bare
/// full HTML document with no layout.
/// </summary>
public sealed class ViewRenderService(
    IRazorViewEngine viewEngine,
    ITempDataProvider tempDataProvider,
    ILogger<ViewRenderService> logger)
{
    public async Task<string> RenderAsync(HttpContext httpContext, string viewName, object model)
    {
        var routeData = httpContext.GetRouteData() ?? new RouteData();
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
        var viewResult = viewEngine.FindView(actionContext, viewName, isMainPage: false);
        if (!viewResult.Success)
        {
            var locations = string.Join(Environment.NewLine, viewResult.SearchedLocations);
            logger.LogError("Could not find view '{ViewName}' for pixel interstitial. Searched: {Locations}", viewName, locations);
            throw new InvalidOperationException($"Could not find view '{viewName}'. Searched: {locations}");
        }

        await using var writer = new StringWriter();
        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };
        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewData,
            new TempDataDictionary(httpContext, tempDataProvider),
            writer,
            new HtmlHelperOptions());
        await viewResult.View.RenderAsync(viewContext);
        return writer.ToString();
    }
}
