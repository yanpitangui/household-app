using System.ComponentModel.DataAnnotations;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Tasks;

public class TasksIndexModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    ITaskCommands taskCommands,
    ITaskQueries taskQueries,
    IHouseholdQueries householdQueries) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool ShowCompleted { get; set; }

    [BindProperty, Required]
    public string NewTitle { get; set; } = "";

    [BindProperty]
    public Guid? NewAssignedTo { get; set; }

    [BindProperty]
    public DateOnly? NewDueDate { get; set; }

    public IReadOnlyList<TaskSummary> Tasks { get; private set; } = [];
    public IReadOnlyList<HouseholdMemberDto> Members { get; private set; } = [];
    public Dictionary<Guid, string> MemberNames { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await Load();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!ModelState.IsValid)
        {
            await Load();
            return Page();
        }

        DateTimeOffset? dueDate = NewDueDate.HasValue
            ? new DateTimeOffset(NewDueDate.Value, TimeOnly.MinValue, TimeSpan.Zero)
            : null;

        await taskCommands.CreateTaskAsync(HouseholdId, NewTitle, null, NewAssignedTo, dueDate);
        return RedirectToPage(new { householdId = HouseholdId, showCompleted = ShowCompleted });
    }

    public async Task<IActionResult> OnPostCompleteAsync(Guid taskId)
    {
        await taskCommands.CompleteTaskAsync(taskId);
        return RedirectToPage(new { householdId = HouseholdId, showCompleted = ShowCompleted });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid taskId)
    {
        await taskCommands.DeleteTaskAsync(taskId);
        return RedirectToPage(new { householdId = HouseholdId, showCompleted = ShowCompleted });
    }

    private async Task Load()
    {
        var tasksTask = taskQueries.ListAsync(HouseholdId, ShowCompleted);
        var membersTask = householdQueries.GetMembersAsync(HouseholdId);
        await Task.WhenAll(tasksTask, membersTask);
        Tasks = tasksTask.Result;
        Members = membersTask.Result;
        MemberNames = Members.ToDictionary(m => m.UserId, m => m.DisplayName);
    }
}
