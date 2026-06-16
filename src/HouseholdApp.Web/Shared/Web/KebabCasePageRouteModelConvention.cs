using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace HouseholdApp.Web.Shared.Web;

public sealed partial class KebabCasePageRouteModelConvention : IPageRouteModelConvention
{
    public void Apply(PageRouteModel model)
    {
        foreach (var selector in model.Selectors)
        {
            if (selector.AttributeRouteModel?.Template is { } template)
                selector.AttributeRouteModel.Template = ToKebabCaseRoute(template);
        }
    }

    private static string ToKebabCaseRoute(string template)
    {
        var segments = template.Split('/');
        return string.Join('/', segments.Select(ToKebabCaseSegment));
    }

    private static string ToKebabCaseSegment(string segment)
    {
        if (string.IsNullOrEmpty(segment) || segment.StartsWith('{'))
            return segment;
        return PascalBoundary().Replace(segment, "$1-$2").ToLowerInvariant();
    }

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex PascalBoundary();
}
