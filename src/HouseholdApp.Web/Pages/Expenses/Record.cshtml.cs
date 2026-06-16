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

    [BindProperty, Required]
    public string Description { get; set; } = "";

    [BindProperty]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [BindProperty, Range(0.01, double.MaxValue)]
    public decimal AmountReais { get; set; }

    [BindProperty]
    public Guid GroupId { get; set; }

    [BindProperty]
    public Guid PayerId { get; set; }

    [BindProperty]
    public List<Guid> SplitMemberIds { get; set; } = [];

    public IReadOnlyList<ExpenseGroupSummary> Groups { get; private set; } = [];
    public IReadOnlyList<HouseholdMemberDto> Members { get; private set; } = [];

    public async Task OnGetAsync()
    {
        PayerId = CurrentUserId;
        await LoadLookups();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (SplitMemberIds.Count == 0)
            ModelState.AddModelError("SplitMemberIds", "Select at least one member.");
        if (!ModelState.IsValid)
        {
            await LoadLookups();
            return Page();
        }

        var cents = (long)(AmountReais * 100);
        var share = cents / SplitMemberIds.Count;
        var remainder = cents % SplitMemberIds.Count;

        var funding = new[] { new FundingSourceDto(PayerId, cents) };
        var allocations = SplitMemberIds
            .Select((id, i) => new AllocationDto(id, share + (i == 0 ? remainder : 0)))
            .ToList();

        await expenseCommands.RecordExpenseAsync(
            HouseholdId, GroupId, Description,
            new DateTimeOffset(Date, TimeOnly.MinValue, TimeSpan.Zero),
            funding, allocations);

        TempData["Success"] = "Expense recorded.";
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
