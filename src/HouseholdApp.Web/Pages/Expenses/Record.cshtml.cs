using System.ComponentModel.DataAnnotations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Marten.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Expenses;

public class RecordExpenseModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IExpenseCommands expenseCommands,
    IExpenseQueries expenseQueries,
    IInstallmentPlanCommands planCommands,
    IHouseholdQueries householdQueries,
    TimeProvider timeProvider) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? ExpenseId { get; set; }

    [BindProperty, Required]
    public string Description { get; set; } = "";

    [BindProperty]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

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

    [BindProperty]
    public string Repeat { get; set; } = "once";

    [BindProperty]
    public int InstallmentCount { get; set; } = 2;

    [BindProperty]
    public bool AmountIsPerInstallment { get; set; }

    [BindProperty]
    public DateOnly? FirstInstallmentDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? PlanId { get; set; }

    public InstallmentPlanPage? EditingPlan { get; private set; }

    /// <summary>The remaining amount split in proportion to the plan's per-installment split, seeding the split panel.</summary>
    public IReadOnlyList<Allocation> EditingPlanAllocations { get; private set; } = [];

    public IReadOnlyList<ExpenseGroupSummary> Groups { get; private set; } = [];
    public IReadOnlyList<HouseholdMemberDto> Members { get; private set; } = [];
    public ExpenseDetail? Editing { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid? group)
    {
        PayerId = CurrentUserId;
        if (group is { } presetGroupId) GroupId = presetGroupId;

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

        if (PlanId.HasValue)
        {
            if (await LoadEditablePlanAsync() is { } ended) return ended;
            Description = EditingPlan!.Summary.Description;
            GroupId = EditingPlan.ExpenseGroupId;
            PayerId = EditingPlan.PayerId;
        }

        await LoadLookups();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (PlanId.HasValue && await LoadEditablePlanAsync() is { } ended) return ended;

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

        if (PlanId is { } editPlanId)
        {
            try
            {
                await planCommands.UpdateAsync(HouseholdId, editPlanId, Description, PayerId, allocations);
            }
            catch (InvalidOperationException ex)
            {
                await LoadLookups();
                ModelState.AddModelError("", ex.Message);
                return Page();
            }
            catch (StreamLockedException)
            {
                TempData["Error"] = Loc["Expenses.Plan.Busy"].Value;
                return RedirectToPage("Plan", new { householdId = HouseholdId, planId = editPlanId });
            }

            TempData["Success"] = Loc["Flash.PlanUpdated"].Value;
            return RedirectToPage("Plan", new { householdId = HouseholdId, planId = editPlanId });
        }

        if (Repeat == "installments" && !ExpenseId.HasValue)
        {
            await LoadLookups();
            if (Groups.All(g => g.Id != GroupId))
            {
                ModelState.AddModelError("", Loc["Expenses.Group.Gone"].Value);
                return Page();
            }

            // Per-installment mode: the form holds one installment, so amount and shares scale by the count.
            var multiplier = AmountIsPerInstallment ? InstallmentCount : 1;
            try
            {
                var planId = await planCommands.CreateAsync(
                    HouseholdId, GroupId, Description, PayerId, cents * multiplier, InstallmentCount,
                    Date, FirstInstallmentDate ?? Date,
                    allocations.Select(a => a with { Cents = a.Cents * multiplier }).ToList());
                TempData["Success"] = Loc["Flash.PlanCreated"].Value;
                return RedirectToPage("Plan", new { householdId = HouseholdId, planId });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("", ex.Message);
                return Page();
            }
        }

        if (ExpenseId.HasValue)
        {
            var existing = await expenseQueries.GetExpenseAsync(ExpenseId.Value);
            if (existing is null || existing.IsVoided || existing.HouseholdId != HouseholdId)
            {
                TempData["Error"] = Loc["Expenses.CannotEditVoided"].Value;
                return RedirectToPage("Index", new { householdId = HouseholdId });
            }

            await expenseCommands.EditExpenseAsync(HouseholdId, ExpenseId.Value, Description, date, funding, allocations);
            TempData["Success"] = Loc["Flash.ExpenseUpdated"].Value;
            return RedirectToPage("Group", new { householdId = HouseholdId, groupId = existing.ExpenseGroupId });
        }

        await LoadLookups();
        if (Groups.All(g => g.Id != GroupId))
        {
            ModelState.AddModelError("", Loc["Expenses.Group.Gone"].Value);
            return Page();
        }

        await expenseCommands.RecordExpenseAsync(HouseholdId, GroupId, Description, date, funding, allocations);
        TempData["Success"] = Loc["Flash.ExpenseRecorded"].Value;
        return RedirectToPage("Group", new { householdId = HouseholdId, groupId = GroupId });
    }

    private void SetEditingPlan(InstallmentPlanPage plan)
    {
        EditingPlan = plan;
        EditingPlanAllocations = InstallmentSchedule.Distribute(
            plan.Summary.RemainingCents,
            plan.SplitPerInstallment.Select(s => new Allocation(s.UserId, s.Cents)).ToList());
    }

    /// <summary>
    /// Loads the plan being edited and pins Amount to what is still remaining, so a re-render after a failed POST
    /// never carries a stale amount. Returns a redirect when the plan can no longer be edited.
    /// </summary>
    private async Task<IActionResult?> LoadEditablePlanAsync()
    {
        var planId = PlanId!.Value;
        var plan = await expenseQueries.GetInstallmentPlanPageAsync(HouseholdId, planId, CurrentUserId);
        if (plan is null)
        {
            TempData["Error"] = Loc["Expenses.Plan.NotFound"].Value;
            return RedirectToPage("Index", new { householdId = HouseholdId });
        }

        if (plan.Summary.Status != InstallmentPlanStatus.Active || plan.Summary.RemainingCents == 0)
        {
            TempData["Error"] = Loc["Expenses.Plan.Ended"].Value;
            return RedirectToPage("Plan", new { householdId = HouseholdId, planId });
        }

        SetEditingPlan(plan);
        Amount = plan.Summary.RemainingCents / 100m;
        return null;
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
