using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Marten.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Expenses;

public class InstallmentPlanModel(
    TimeProvider timeProvider,
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IExpenseQueries expenseQueries,
    IActivityFeedQueries activityQueries,
    IInstallmentPlanCommands planCommands) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid PlanId { get; set; }

    public InstallmentPlanPage Plan { get; private set; } = default!;
    public IReadOnlyList<ActivityFeedItem> History { get; private set; } = [];
    public DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    public async Task<IActionResult> OnGetAsync()
    {
        var plan = await expenseQueries.GetInstallmentPlanPageAsync(HouseholdId, PlanId, CurrentUserId);
        if (plan is null) return NotFound();
        Plan = plan;
        History = await activityQueries.GetPlanHistoryAsync(HouseholdId, PlanId, CurrentUserId);
        return Page();
    }

    public async Task<IActionResult> OnPostPayEarlyAsync(decimal amount)
    {
        var plan = await expenseQueries.GetInstallmentPlanPageAsync(HouseholdId, PlanId, CurrentUserId);
        if (plan is null) return NotFound();

        var range = $"{plan.NextNumber}–{plan.Summary.Count}";
        var description = Loc["Expenses.Plan.PayoffName", plan.Summary.Description, range, plan.Summary.Count].Value;
        return await RunAsync(
            plan.Summary.Status,
            () => planCommands.PayRemainingEarlyAsync(HouseholdId, PlanId, (long)Math.Round(amount * 100), description),
            "Flash.PlanPaidEarly");
    }

    public async Task<IActionResult> OnPostCancelAsync(bool voidCreated)
    {
        var plan = await expenseQueries.GetInstallmentPlanPageAsync(HouseholdId, PlanId, CurrentUserId);
        if (plan is null) return NotFound();

        return await RunAsync(
            plan.Summary.Status,
            () => planCommands.CancelAsync(HouseholdId, PlanId, voidCreated),
            "Flash.PlanCancelled");
    }

    // A stale page (plan already ended, double submit) lands back on the plan page showing its current state.
    private async Task<IActionResult> RunAsync(InstallmentPlanStatus status, Func<Task> action, string successKey)
    {
        if (status != InstallmentPlanStatus.Active)
        {
            TempData["Error"] = Loc["Expenses.Plan.Ended"].Value;
            return BackToPlan();
        }

        try
        {
            await action();
            TempData["Success"] = Loc[successKey].Value;
        }
        catch (InvalidOperationException ex)
        {
            var current = await expenseQueries.GetInstallmentPlanPageAsync(HouseholdId, PlanId, CurrentUserId);
            TempData["Error"] = current is { Summary.Status: not InstallmentPlanStatus.Active }
                ? Loc["Expenses.Plan.Ended"].Value
                : ex.Message;
        }
        catch (Exception ex) when (ex is StreamLockedException or JasperFx.ConcurrencyException)
        {
            TempData["Error"] = Loc["Expenses.Plan.Busy"].Value;
        }

        return BackToPlan();
    }

    private IActionResult BackToPlan() => RedirectToPage(new { householdId = HouseholdId, planId = PlanId });
}
