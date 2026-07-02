using System.ComponentModel.DataAnnotations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Expenses;

public class RecordExpenseModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IExpenseCommands expenseCommands,
    IExpenseQueries expenseQueries,
    IHouseholdQueries householdQueries) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? ExpenseId { get; set; }

    [BindProperty, Required]
    public string Description { get; set; } = "";

    [BindProperty]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [BindProperty, Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    [BindProperty]
    public Guid GroupId { get; set; }

    [BindProperty]
    public Guid PayerId { get; set; }

    [BindProperty]
    public List<Guid> SplitMemberIds { get; set; } = [];

    [BindProperty]
    public List<Guid> AllocationUserIds { get; set; } = [];

    [BindProperty]
    public List<long> AllocationCents { get; set; } = [];

    public IReadOnlyList<ExpenseGroupSummary> Groups { get; private set; } = [];
    public IReadOnlyList<HouseholdMemberDto> Members { get; private set; } = [];
    public ExpenseDetail? Editing { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        PayerId = CurrentUserId;

        if (ExpenseId.HasValue)
        {
            var existing = await expenseQueries.GetExpenseAsync(ExpenseId.Value);
            if (existing is null || existing.IsVoided || existing.HouseholdId != HouseholdId)
            {
                TempData["Error"] = Loc["Expenses.CannotEditVoided"].Value;
                return RedirectToPage("Index", new { householdId = HouseholdId });
            }

            Editing = existing;
            Description = existing.Description;
            Date = DateOnly.FromDateTime(existing.Date.Date);
            Amount = existing.TotalCents / 100m;
            GroupId = existing.ExpenseGroupId;
            PayerId = existing.FundingSources.Count > 0 ? existing.FundingSources[0].UserId : CurrentUserId;
        }

        await LoadLookups();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadLookups();
            return Page();
        }

        var cents = (long)(Amount * 100);
        var funding = new[] { new FundingSourceDto(PayerId, cents) };

        List<AllocationDto> allocations;

        if (AllocationUserIds.Count > 0 && AllocationUserIds.Count == AllocationCents.Count)
        {
            var sum = AllocationCents.Sum();
            if (sum != cents)
            {
                ModelState.AddModelError("", "Allocation amounts do not sum to the total.");
                await LoadLookups();
                return Page();
            }
            allocations = AllocationUserIds
                .Zip(AllocationCents, (id, c) => new AllocationDto(id, c))
                .ToList();
        }
        else
        {
            if (SplitMemberIds.Count == 0)
            {
                ModelState.AddModelError("SplitMemberIds", "Select at least one member.");
                await LoadLookups();
                return Page();
            }
            var share = cents / SplitMemberIds.Count;
            var remainder = cents % SplitMemberIds.Count;
            allocations = SplitMemberIds
                .Select((id, i) => new AllocationDto(id, share + (i == 0 ? remainder : 0)))
                .ToList();
        }

        var date = new DateTimeOffset(Date, TimeOnly.MinValue, TimeSpan.Zero);

        if (ExpenseId.HasValue)
        {
            var existing = await expenseQueries.GetExpenseAsync(ExpenseId.Value);
            if (existing is null || existing.IsVoided || existing.HouseholdId != HouseholdId)
            {
                TempData["Error"] = Loc["Expenses.CannotEditVoided"].Value;
                return RedirectToPage("Index", new { householdId = HouseholdId });
            }

            await expenseCommands.EditExpenseAsync(ExpenseId.Value, Description, date, funding, allocations);
            TempData["Success"] = Loc["Flash.ExpenseUpdated"].Value;
        }
        else
        {
            await expenseCommands.RecordExpenseAsync(HouseholdId, GroupId, Description, date, funding, allocations);
            TempData["Success"] = Loc["Flash.ExpenseRecorded"].Value;
        }

        return RedirectToPage("Index", new { householdId = HouseholdId });
    }

    private async Task LoadLookups()
    {
        var groupsTask = expenseQueries.ListExpenseGroupsAsync(HouseholdId);
        var membersTask = householdQueries.GetMembersAsync(HouseholdId);
        await Task.WhenAll(groupsTask, membersTask);
        Groups = groupsTask.Result;
        Members = membersTask.Result;
    }
}
