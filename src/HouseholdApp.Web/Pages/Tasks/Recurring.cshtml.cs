using System.ComponentModel.DataAnnotations;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Domain;
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
    IHouseholdQueries householdQueries,
    TimeProvider time) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty, Required]
    public string NewTitle { get; set; } = "";

    [BindProperty]
    public string? NewDescription { get; set; }

    [BindProperty]
    public Guid? NewAssignedTo { get; set; }

    [BindProperty]
    public RecurrenceFrequency NewFrequency { get; set; } = RecurrenceFrequency.Weekly;

    [BindProperty]
    public DateOnly NewFirstDate { get; set; }

    [BindProperty]
    public TimeOnly NewTime { get; set; } = new(12, 0);

    public IReadOnlyList<RecurringTaskSummary> RecurringTasks { get; private set; } = [];
    public IReadOnlyList<HouseholdMemberDto> Members { get; private set; } = [];

    public async Task OnGetAsync()
    {
        NewFirstDate = Today();
        await Load();
    }

    [RequireManage]
    public async Task<IActionResult> OnPostCreateAsync()
    {
        var today = Today();
        if (NewFirstDate != default && NewFirstDate < today)
            ModelState.AddModelError(nameof(NewFirstDate), Loc["Recurrence.FirstDatePast"].Value);

        if (!ModelState.IsValid)
        {
            if (NewFirstDate == default) NewFirstDate = today;
            await Load();
            return Page();
        }

        await taskCommands.CreateRecurringTaskAsync(
            HouseholdId, NewTitle, NewDescription, NewAssignedTo,
            new RecurrenceSchedule(NewFrequency, NewFirstDate, NewTime));

        TempData["Success"] = Loc["Flash.RecurringTaskCreated"].Value;
        return RedirectToPage(new { householdId = HouseholdId });
    }

    [RequireManage]
    public async Task<IActionResult> OnPostDeactivateAsync(Guid recurringTaskId)
    {
        try
        {
            await taskCommands.DeactivateRecurringTaskAsync(HouseholdId, recurringTaskId);
            TempData["Success"] = Loc["Flash.RecurringTaskDeactivated"].Value;
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        return RedirectToPage(new { householdId = HouseholdId });
    }

    private DateOnly Today() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    private async Task Load()
    {
        var recurringTask = taskQueries.ListRecurringAsync(HouseholdId);
        var membersTask = householdQueries.GetMembersAsync(HouseholdId);
        await Task.WhenAll(recurringTask, membersTask);
        RecurringTasks = recurringTask.Result;
        Members = membersTask.Result;
    }
}
