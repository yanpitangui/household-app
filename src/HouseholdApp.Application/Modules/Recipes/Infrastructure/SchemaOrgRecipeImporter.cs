using AngleSharp.Html.Parser;
using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using System.Net;
using System.Text.Json;

namespace HouseholdApp.Application.Modules.Recipes.Infrastructure;

public sealed class SchemaOrgRecipeImporter(HttpClient httpClient) : IRecipeImporter
{
    public async Task<RecipeImportResult> ImportAsync(string url, CancellationToken ct = default)
    {
        string html;
        try
        {
            html = await httpClient.GetStringAsync(url, ct);
        }
        catch (Exception ex)
        {
            return new RecipeImportResult(false, $"Could not fetch URL: {ex.Message}");
        }

        var parser = new HtmlParser();
        var document = await parser.ParseDocumentAsync(html, ct);

        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            var content = script.TextContent;
            if (string.IsNullOrWhiteSpace(content)) continue;

            try
            {
                using var doc = JsonDocument.Parse(content);
                var recipeNode = FindRecipeNode(doc.RootElement);
                if (recipeNode is null) continue;
                return ParseRecipe(recipeNode.Value, url);
            }
            catch (JsonException)
            {
                continue;
            }
        }

        return new RecipeImportResult(false, "No recipe data found on this page.");
    }

    private static JsonElement? FindRecipeNode(JsonElement root)
    {
        // Some sites emit a top-level array instead of a single object
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var found = FindRecipeNode(item);
                if (found is not null) return found;
            }
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (root.TryGetProperty("@type", out var type) && IsRecipeType(type))
            return root;

        if (root.TryGetProperty("@graph", out var graph) &&
            graph.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in graph.EnumerateArray())
            {
                if (item.TryGetProperty("@type", out var t) && IsRecipeType(t))
                    return item;
            }
        }

        return null;
    }

    // @type can be a string "Recipe" or an array ["Recipe", "Thing"]
    private static bool IsRecipeType(JsonElement type)
    {
        if (type.ValueKind == JsonValueKind.String)
            return type.GetString()?.Equals("Recipe", StringComparison.OrdinalIgnoreCase) == true;

        if (type.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in type.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String &&
                    item.GetString()?.Equals("Recipe", StringComparison.OrdinalIgnoreCase) == true)
                    return true;
            }
        }

        return false;
    }

    private static string? DecodeHtml(string? text)
    {
        if (text is null) return null;
        string decoded;
        do { decoded = text; text = WebUtility.HtmlDecode(decoded); } while (text != decoded);
        return text;
    }

    private static RecipeImportResult ParseRecipe(JsonElement recipe, string sourceUrl)
    {
        var title       = DecodeHtml(recipe.TryGetProperty("name",        out var n) ? n.GetString() : null);
        var description = DecodeHtml(recipe.TryGetProperty("description", out var d) ? d.GetString() : null);
        var servings    = recipe.TryGetProperty("recipeYield", out var y) ? ExtractServings(y) : null;
        var schemaUrl   = recipe.TryGetProperty("url",         out var u) ? u.GetString()      : null;

        var ingredients = new List<string>();
        if (recipe.TryGetProperty("recipeIngredient", out var ing) &&
            ing.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in ing.EnumerateArray())
            {
                var s = DecodeHtml(item.GetString());
                if (!string.IsNullOrWhiteSpace(s)) ingredients.Add(s.Trim());
            }
        }

        var instructions = recipe.TryGetProperty("recipeInstructions", out var instr)
            ? ExtractInstructions(instr)
            : "";

        return new RecipeImportResult(
            Success: true,
            Title: title,
            Description: description,
            Servings: servings,
            SourceUrl: schemaUrl ?? sourceUrl,
            Ingredients: ingredients,
            Instructions: instructions);
    }

    private static string ExtractServings(JsonElement yield)
    {
        return yield.ValueKind switch
        {
            JsonValueKind.String => yield.GetString() ?? "",
            JsonValueKind.Number => yield.GetRawText(),
            JsonValueKind.Array  => yield.EnumerateArray().FirstOrDefault() is var first &&
                                    first.ValueKind != JsonValueKind.Undefined
                                    ? first.GetRawText().Trim('"') : "",
            _                    => ""
        };
    }

    private static string ExtractInstructions(JsonElement instr)
    {
        if (instr.ValueKind == JsonValueKind.String)
            return DecodeHtml(instr.GetString()) ?? "";

        if (instr.ValueKind == JsonValueKind.Array)
        {
            var steps = new List<string>();
            CollectSteps(instr, steps);
            return string.Join("\n\n", steps);
        }

        return "";
    }

    // Recursively collect step texts, handling HowToStep, HowToSection, HowToDirection,
    // itemListElement nesting, and plain strings — for maximum site compatibility.
    private static void CollectSteps(JsonElement node, List<string> steps)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
                CollectSteps(item, steps);
            return;
        }

        if (node.ValueKind == JsonValueKind.String)
        {
            var s = DecodeHtml(node.GetString());
            if (!string.IsNullOrWhiteSpace(s)) steps.Add(s!.Trim());
            return;
        }

        if (node.ValueKind != JsonValueKind.Object) return;

        // HowToSection — recurse into its itemListElement
        if (node.TryGetProperty("@type", out var type) &&
            type.ValueKind == JsonValueKind.String &&
            type.GetString()?.Equals("HowToSection", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (node.TryGetProperty("itemListElement", out var sectionItems))
                CollectSteps(sectionItems, steps);
            return;
        }

        // Try direct text / name
        var text = ExtractStepText(node);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var decoded = DecodeHtml(text);
            if (!string.IsNullOrWhiteSpace(decoded)) steps.Add(decoded!.Trim());
            return;
        }

        // itemListElement nesting without a top-level text
        if (node.TryGetProperty("itemListElement", out var ile))
            CollectSteps(ile, steps);
    }

    private static string? ExtractStepText(JsonElement item)
    {
        // text is preferred; name is a fallback some sites use
        if (item.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
            return t.GetString();

        if (item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
            return n.GetString();

        return null;
    }
}
