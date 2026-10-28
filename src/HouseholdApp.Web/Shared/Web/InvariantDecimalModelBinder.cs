using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HouseholdApp.Web.Shared.Web;

// Decimals bind with the invariant culture so number inputs, which always submit a dot decimal ("312.40"),
// parse whatever the request culture is.
public sealed class InvariantDecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext ctx)
    {
        var result = ctx.ValueProvider.GetValue(ctx.ModelName);
        if (result == ValueProviderResult.None) return Task.CompletedTask;

        ctx.ModelState.SetModelValue(ctx.ModelName, result);
        var raw = result.FirstValue;
        if (string.IsNullOrWhiteSpace(raw)) return Task.CompletedTask;

        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            ctx.Result = ModelBindingResult.Success(value);
        else
            ctx.ModelState.TryAddModelError(ctx.ModelName,
                ctx.ModelMetadata.ModelBindingMessageProvider.AttemptedValueIsInvalidAccessor(
                    raw, ctx.ModelMetadata.DisplayName ?? ctx.ModelName));

        return Task.CompletedTask;
    }
}

public sealed class InvariantDecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context) =>
        context.Metadata.UnderlyingOrModelType == typeof(decimal) ? new InvariantDecimalModelBinder() : null;
}
