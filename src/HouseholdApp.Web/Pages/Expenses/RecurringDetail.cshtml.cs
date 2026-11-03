using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
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

    public Task<IActionResult> OnPostEndAsync() =>
        RunAsync(s => s != RecurringExpenseStatus.Ended, recurringCommands.EndAsync, "Expenses.Recurring.EndedFlash");

    public Task<IActionResult> OnPostPauseAsync() =>
        RunAsync(s => s == RecurringExpenseStatus.Active, recurringCommands.PauseAsync, "Expenses.Recurring.PausedFlash");

    public Task<IActionResult> OnPostResumeAsync() =>
        RunAsync(s => s == RecurringExpenseStatus.Paused, recurringCommands.ResumeAsync, "Expenses.Recurring.ResumedFlash");

    // A stale page (someone else already changed the status) gets the flash for the current status instead of an error.
    private async Task<IActionResult> RunAsync(
        Func<RecurringExpenseStatus, bool> allowed,
        Func<Guid, Guid, CancellationToken, Task> command,
        string successKey)
    {
        var recurring = await expenseQueries.GetRecurringExpensePageAsync(HouseholdId, RecurringId, CurrentUserId);
        if (recurring is null) return NotFound();
        if (!allowed(recurring.Status))
        {
            TempData["Error"] = StaleMessage(recurring.Status);
            return BackToRecurring();
        }

        try
        {
            await command(HouseholdId, RecurringId, HttpContext.RequestAborted);
            TempData["Success"] = Loc[successKey].Value;
        }
        catch (InvalidOperationException)
        {
            var current = await expenseQueries.GetRecurringExpensePageAsync(HouseholdId, RecurringId, CurrentUserId);
            if (current is null) return NotFound();
            TempData["Error"] = allowed(current.Status)
                ? Loc["Expenses.Recurring.Busy"].Value
                : StaleMessage(current.Status);
        }
        catch (Exception ex) when (ex is StreamLockedException or JasperFx.ConcurrencyException)
        {
            TempData["Error"] = Loc["Expenses.Recurring.Busy"].Value;
        }

        return BackToRecurring();
    }

    private string StaleMessage(RecurringExpenseStatus status) => status switch
    {
        RecurringExpenseStatus.Ended => Loc["Expenses.Recurring.AlreadyEnded"].Value,
        RecurringExpenseStatus.Paused => Loc["Expenses.Recurring.AlreadyPaused"].Value,
        _ => Loc["Expenses.Recurring.AlreadyActive"].Value
    };

    private IActionResult BackToRecurring() => RedirectToPage(new { householdId = HouseholdId, recurringId = RecurringId });
}
