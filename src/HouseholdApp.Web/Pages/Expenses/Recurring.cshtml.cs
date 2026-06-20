using System.ComponentModel.DataAnnotations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Expenses;

public class RecurringExpensesModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IExpenseCommands expenseCommands,
    IExpenseQueries expenseQueries,
    IHouseholdQueries householdQueries,
    TimeProvider time) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty, Required]
    public string NewDescription { get; set; } = "";

    [BindProperty]
    public Guid NewGroupId { get; set; }

    [BindProperty]
    public RecurrenceFrequency NewFrequency { get; set; } = RecurrenceFrequency.Monthly;

    [BindProperty]
    public DateTimeOffset NewStartAt { get; set; }

    [BindProperty, Range(0.01, double.MaxValue)]
    public decimal NewAmount { get; set; }

    [BindProperty]
    public Guid NewPayerId { get; set; }

    [BindProperty]
    public List<Guid> NewSplitMemberIds { get; set; } = [];

    // Edit bindings
    [BindProperty]
    public Guid EditId { get; set; }

    [BindProperty]
    public string EditDescription { get; set; } = "";

    [BindProperty]
    public Guid EditGroupId { get; set; }

    [BindProperty]
    public RecurrenceFrequency EditFrequency { get; set; } = RecurrenceFrequency.Monthly;

    [BindProperty]
    public DateTimeOffset EditStartAt { get; set; }

    [BindProperty, Range(0.01, double.MaxValue)]
    public decimal EditAmount { get; set; }

    [BindProperty]
    public Guid EditPayerId { get; set; }

    [BindProperty]
    public List<Guid> EditSplitMemberIds { get; set; } = [];

    public IReadOnlyList<RecurringExpenseSummary> RecurringExpenses { get; private set; } = [];
    public IReadOnlyList<ExpenseGroupSummary> Groups { get; private set; } = [];
    public IReadOnlyList<HouseholdMemberDto> Members { get; private set; } = [];
    public Dictionary<Guid, string> GroupNames { get; private set; } = [];

    public async Task OnGetAsync()
    {
        NewPayerId = CurrentUserId;
        NewStartAt = time.GetUtcNow().AddDays(1);
        await Load();
    }

    [RequireManage]
    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (NewSplitMemberIds.Count == 0)
            ModelState.AddModelError("NewSplitMemberIds", "Select at least one member to split with.");
        if (!ModelState.IsValid)
        {
            await Load();
            return Page();
        }

        var cents = (long)(NewAmount * 100);
        var share = cents / NewSplitMemberIds.Count;
        var remainder = cents % NewSplitMemberIds.Count;

        var funding = new[] { new FundingSourceDto(NewPayerId, cents) };
        var allocations = NewSplitMemberIds
            .Select((id, i) => new AllocationDto(id, share + (i == 0 ? remainder : 0)))
            .ToList();

        await expenseCommands.CreateRecurringExpenseAsync(
            HouseholdId, NewGroupId, NewDescription, NewFrequency, NewStartAt, funding, allocations);

        TempData["Success"] = Loc["Flash.RecurringExpenseCreated"].Value;
        return RedirectToPage(new { householdId = HouseholdId });
    }

    [RequireManage]
    public async Task<IActionResult> OnPostUpdateAsync()
    {
        if (EditSplitMemberIds.Count == 0)
            ModelState.AddModelError("EditSplitMemberIds", "Select at least one member to split with.");
        if (!ModelState.IsValid)
        {
            await Load();
            return Page();
        }

        var cents = (long)(EditAmount * 100);
        var share = cents / EditSplitMemberIds.Count;
        var remainder = cents % EditSplitMemberIds.Count;

        var funding = new[] { new FundingSourceDto(EditPayerId, cents) };
        var allocations = EditSplitMemberIds
            .Select((id, i) => new AllocationDto(id, share + (i == 0 ? remainder : 0)))
            .ToList();

        await expenseCommands.UpdateRecurringExpenseAsync(
            EditId, EditDescription, EditFrequency, EditStartAt, funding, allocations);

        TempData["Success"] = Loc["Flash.RecurringExpenseUpdated"].Value;
        return RedirectToPage(new { householdId = HouseholdId });
    }

    [RequireManage]
    public async Task<IActionResult> OnPostDeactivateAsync(Guid recurringExpenseId)
    {
        await expenseCommands.DeactivateRecurringExpenseAsync(recurringExpenseId);
        TempData["Success"] = Loc["Flash.RecurringExpenseDeactivated"].Value;
        return RedirectToPage(new { householdId = HouseholdId });
    }

    private async Task Load()
    {
        var recurringTask = expenseQueries.ListRecurringExpensesAsync(HouseholdId);
        var groupsTask = expenseQueries.ListExpenseGroupsAsync(HouseholdId);
        var membersTask = householdQueries.GetMembersAsync(HouseholdId);
        await Task.WhenAll(recurringTask, groupsTask, membersTask);
        RecurringExpenses = recurringTask.Result;
        Groups = groupsTask.Result;
        GroupNames = Groups.ToDictionary(g => g.Id, g => g.Name);
        Members = membersTask.Result;
    }
}
