using HouseholdApp.Application.Modules.Tasks.Application.Ports;

namespace HouseholdApp.Web.Pages.Tasks;

public sealed record TasksListViewModel(
    Guid HouseholdId,
    IReadOnlyList<TaskSummary> Tasks,
    Dictionary<Guid, string> MemberNames,
    bool ShowCompleted);
