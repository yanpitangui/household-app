using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Web.Shared.Web;
using Marten.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Expenses;

public class RecurringDetailModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IExpenseQueries expenseQueries,
    IActivityFeedQueries activityQueries,
    IRecurringExpenseCommands recurringCommands) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid RecurringId { get; set; }

    public RecurringExpensePage Recurring { get; private set; } = default!;
    public IReadOnlyList<ActivityFeedItem> History { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var recurring = await expenseQueries.GetRecurringExpensePageAsync(HouseholdId, RecurringId, CurrentUserId);
        if (recurring is null) return NotFound();
        Recurring = recurring;
        History = await activityQueries.GetRecurringHistoryAsync(HouseholdId, RecurringId, CurrentUserId);
        return Page();
    }

    public async Task<IActionResult> OnPostEndAsync()
    {
        var recurring = await expenseQueries.GetRecurringExpensePageAsync(HouseholdId, RecurringId, CurrentUserId);
        if (recurring is null) return NotFound();
        if (!recurring.IsActive)
        {
            TempData["Error"] = Loc["Expenses.Recurring.AlreadyEnded"].Value;
            return BackToRecurring();
        }

        try
        {
            await recurringCommands.EndAsync(HouseholdId, RecurringId);
            TempData["Success"] = Loc["Expenses.Recurring.EndedFlash"].Value;
        }
        catch (InvalidOperationException)
        {
            var current = await expenseQueries.GetRecurringExpensePageAsync(HouseholdId, RecurringId, CurrentUserId);
            if (current is null) return NotFound();
            TempData["Error"] = current.IsActive
                ? Loc["Expenses.Plan.Busy"].Value
                : Loc["Expenses.Recurring.AlreadyEnded"].Value;
        }
        catch (Exception ex) when (ex is StreamLockedException or JasperFx.ConcurrencyException)
        {
            TempData["Error"] = Loc["Expenses.Plan.Busy"].Value;
        }

        return BackToRecurring();
    }

    private IActionResult BackToRecurring() => RedirectToPage(new { householdId = HouseholdId, recurringId = RecurringId });
}
