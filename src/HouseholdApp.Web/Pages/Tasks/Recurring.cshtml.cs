using System.ComponentModel.DataAnnotations;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Tasks;

public class RecurringTasksModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    ITaskCommands taskCommands,
    ITaskQueries taskQueries,
    IHouseholdQueries householdQueries) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty, Required]
    public string NewTitle { get; set; } = "";

    [BindProperty]
    public string? NewDescription { get; set; }

    [BindProperty]
    public Guid? NewAssignedTo { get; set; }

    [BindProperty, Required]
    public string NewCronExpression { get; set; } = "";

    public IReadOnlyList<RecurringTaskSummary> RecurringTasks { get; private set; } = [];
    public IReadOnlyList<HouseholdMemberDto> Members { get; private set; } = [];

    public async Task OnGetAsync() => await Load();

    [RequireManage]
    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!ModelState.IsValid)
        {
            await Load();
            return Page();
        }

        await taskCommands.CreateRecurringTaskAsync(
            HouseholdId, NewTitle, NewDescription, NewAssignedTo, NewCronExpression);

        TempData["Success"] = "Recurring task created.";
        return RedirectToPage(new { householdId = HouseholdId });
    }

    [RequireManage]
    public async Task<IActionResult> OnPostDeactivateAsync(Guid recurringTaskId)
    {
        await taskCommands.DeactivateRecurringTaskAsync(recurringTaskId);
        TempData["Success"] = "Recurring task deactivated.";
        return RedirectToPage(new { householdId = HouseholdId });
    }

    private async Task Load()
    {
        var recurringTask = taskQueries.ListRecurringAsync(HouseholdId);
        var membersTask = householdQueries.GetMembersAsync(HouseholdId);
        await Task.WhenAll(recurringTask, membersTask);
        RecurringTasks = recurringTask.Result;
        Members = membersTask.Result;
    }
}
