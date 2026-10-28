using HouseholdApp.Application.Modules.Expenses.Application.Ports;

namespace HouseholdApp.Web.Pages.Expenses;

public sealed record GroupCardsViewModel(Guid HouseholdId, IReadOnlyList<GroupCard> Groups);
