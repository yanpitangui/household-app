using System.ComponentModel.DataAnnotations;
using HouseholdApp.Application.Modules.Recipes.Application.Operations;
using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Recipes;

public class CreateRecipeModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IRecipeCommands recipeCommands,
    IRecipeImporter recipeImporter) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty, Required]
    public string Title { get; set; } = "";

    [BindProperty]
    public string? Description { get; set; }

    [BindProperty]
    public int? Servings { get; set; }

    [BindProperty]
    public string? SourceUrl { get; set; }

    [BindProperty]
    public string? Notes { get; set; }

    [BindProperty]
    public List<string> Ingredients { get; set; } = [];

    [BindProperty]
    public string? InstructionsText { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var ingredients = Ingredients
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => {
                var (qty, unit, name) = IngredientParser.Parse(s.Trim());
                return new IngredientDto(name, qty, unit);
            })
            .ToList();

        var instructions = ParseInstructions(InstructionsText ?? "");

        await recipeCommands.CreateRecipeAsync(
            HouseholdId, Title, Description, Servings, SourceUrl, Notes,
            ingredients, instructions);

        TempData["Success"] = Loc["Flash.RecipeSaved"].Value;
        return RedirectToPage("Index", new { householdId = HouseholdId });
    }

    public async Task<IActionResult> OnPostImportAsync(
        [FromBody] ImportRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Url))
            return new JsonResult(new { success = false, errorMessage = "URL is required" });

        var result = await recipeImporter.ImportAsync(request.Url, ct);
        return new JsonResult(result);
    }

    private static List<InstructionStepDto> ParseInstructions(string text)
    {
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select((line, i) => new InstructionStepDto(i + 1, line.Trim()))
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .ToList();
    }

    public sealed record ImportRequest(string? Url);
}
