using System.Text;
using System.Text.RegularExpressions;
using HouseholdApp.Application.Modules.Expenses.Application.Import;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Expenses;

public sealed record ImportPreview(ImportPlan? Plan, IReadOnlyList<ImportError> Errors)
{
    public bool CanImport => Plan is not null && Errors.Count == 0;
}

[RequestFormLimits(MultipartBodyLengthLimit = 2 * 1024 * 1024)]
[RequestSizeLimit(2 * 1024 * 1024)]
public partial class ImportExpensesModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IExpenseImport expenseImport,
    IHouseholdQueries householdQueries,
    ILogger<ImportExpensesModel> logger) : HouseholdPageModel(currentUser, guard)
{
    private const long MaxFileBytes = 1024 * 1024;

    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty]
    public IFormFile? Upload { get; set; }

    [BindProperty]
    public string? GroupName { get; set; }

    [BindProperty]
    public string? PreviewedFileName { get; set; }

    [BindProperty]
    public List<Guid?> ColumnMembers { get; set; } = [];

    public IReadOnlyList<HouseholdMemberDto> Members { get; private set; } = [];
    public ImportPreview? Preview { get; private set; }
    public bool MappingValid { get; private set; }
    private bool FileChanged { get; set; }

    [RequireManage]
    public void OnGet() { }

    [RequireManage]
    public async Task<IActionResult> OnPostPreviewAsync()
    {
        Preview = await BuildPreviewAsync();
        return Partial("_ImportPreview", this);
    }

    [RequireManage]
    public async Task<IActionResult> OnPostImportAsync()
    {
        Preview = await BuildPreviewAsync();
        // A file that hasn't been previewed yet is shown first so its balance check is seen before importing.
        if (!Preview.CanImport || FileChanged) return Page();

        var plan = Preview.Plan!;
        try
        {
            await expenseImport.ImportAsync(HouseholdId, GroupName!, plan, ColumnMembers.Select(m => m!.Value).ToList());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Any failure after validation may have left the group behind, so point the user at the Groups page to check.
            logger.LogError(ex, "Splitwise import into household {HouseholdId} failed", HouseholdId);
            TempData["Error"] = Loc["Import.Error.ImportFailed"].Value;
            return RedirectToPage("Groups", new { householdId = HouseholdId });
        }
        TempData["Success"] = Loc["Flash.ImportCompleted", plan.EntryCount].Value;
        return RedirectToPage("Index", new { householdId = HouseholdId });
    }

    private async Task<ImportPreview> BuildPreviewAsync()
    {
        Members = await householdQueries.GetMembersAsync(HouseholdId);

        if (Upload is null || Upload.Length == 0)
            return new ImportPreview(null, [new ImportError(ImportErrorCode.NoFile)]);
        if (Upload.Length > MaxFileBytes)
            return new ImportPreview(null, [new ImportError(ImportErrorCode.FileTooLarge)]);

        string text;
        using (var reader = new StreamReader(Upload.OpenReadStream(), Encoding.UTF8))
            text = await reader.ReadToEndAsync();

        var parsed = SplitwiseCsvParser.Parse(text);
        if (parsed.File is null) return new ImportPreview(null, parsed.Errors);

        var planned = SplitwiseImportPlanner.Build(parsed.File);
        if (planned.Plan is null) return new ImportPreview(null, planned.Errors);

        // A new file starts from its own defaults so a previous file's mapping and group name never carry over.
        FileChanged = !string.Equals(Upload.FileName, PreviewedFileName, StringComparison.Ordinal);
        if (FileChanged) GroupName = null;
        if (FileChanged || ColumnMembers.Count != parsed.File.People.Count)
            ColumnMembers = parsed.File.People
                .Select(p => Members.FirstOrDefault(m => string.Equals(m.DisplayName, p, StringComparison.OrdinalIgnoreCase))?.UserId)
                .ToList();
        if (string.IsNullOrWhiteSpace(GroupName))
            GroupName = ExportSuffix().Replace(Path.GetFileNameWithoutExtension(Upload.FileName), "");

        var errors = new List<ImportError>(planned.Errors);
        errors.AddRange(SplitwiseImportPlanner.ValidateMapping(ColumnMembers, Members.Select(m => m.UserId).ToHashSet()));
        if (string.IsNullOrWhiteSpace(GroupName))
            errors.Add(new ImportError(ImportErrorCode.GroupNameEmpty));
        else if (await expenseImport.GroupNameTakenAsync(HouseholdId, GroupName))
            errors.Add(new ImportError(ImportErrorCode.GroupNameTaken, Detail: GroupName.Trim()));

        MappingValid = !errors.Any(e => e.Code is ImportErrorCode.Unmapped or ImportErrorCode.DuplicateMapping or ImportErrorCode.NotAMember);
        return new ImportPreview(planned.Plan, errors);
    }

    // "apartamento_2026-10-27_export" and the browser's duplicate "… (1)" suffix both reduce to "apartamento".
    [GeneratedRegex(@"_\d{4}-\d{2}-\d{2}_export( \(\d+\))?$")]
    private static partial Regex ExportSuffix();
}
