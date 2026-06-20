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

    public async Task OnGetAsync()
    {
        PayerId = CurrentUserId;
        await LoadLookups();
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

        await expenseCommands.RecordExpenseAsync(
            HouseholdId, GroupId, Description,
            new DateTimeOffset(Date, TimeOnly.MinValue, TimeSpan.Zero),
            funding, allocations);

        TempData["Success"] = Loc["Flash.ExpenseRecorded"].Value;
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
