using HouseholdApp.Application.Modules.Expenses.Application.Ports;

namespace HouseholdApp.Web.Pages.Expenses;

/// <summary>One page of activity; PreviousMonth (yyyy-MM) is the last month the page before ended on, so its header is not repeated.</summary>
public sealed record ActivityListViewModel(IReadOnlyList<ActivityFeedItem> Items, string? NextCursor, string? PreviousMonth);

/// <summary>Body of the expense sheet; Sheet is null when the expense is gone. ReturnUrl is where a void lands.</summary>
public sealed record ExpenseSheetViewModel(Guid HouseholdId, ExpenseSheet? Sheet, string ReturnUrl);
