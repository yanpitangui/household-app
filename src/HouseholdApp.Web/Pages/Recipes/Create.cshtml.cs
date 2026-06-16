using System.ComponentModel.DataAnnotations;
using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Recipes;

public class CreateRecipeModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IRecipeCommands recipeCommands) : HouseholdPageModel(currentUser, guard)
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
    public string? IngredientsText { get; set; }

    [BindProperty]
    public string? InstructionsText { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var ingredients = ParseIngredients(IngredientsText ?? "");
        var instructions = ParseInstructions(InstructionsText ?? "");

        await recipeCommands.CreateRecipeAsync(
            HouseholdId, Title, Description, Servings, SourceUrl, Notes,
            ingredients, instructions);

        TempData["Success"] = Loc["Flash.RecipeSaved"].Value;
        return RedirectToPage("Index", new { householdId = HouseholdId });
    }

    private static List<IngredientDto> ParseIngredients(string text)
    {
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var parts = line.Split(',', 3, StringSplitOptions.TrimEntries);
                return new IngredientDto(
                    parts[0],
                    parts.Length > 1 ? parts[1] : null,
                    parts.Length > 2 ? parts[2] : null);
            })
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .ToList();
    }

    private static List<InstructionStepDto> ParseInstructions(string text)
    {
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select((line, i) => new InstructionStepDto(i + 1, line.Trim()))
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .ToList();
    }
}
