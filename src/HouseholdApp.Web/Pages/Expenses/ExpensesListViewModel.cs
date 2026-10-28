using HouseholdApp.Application.Modules.Expenses.Application.Ports;

namespace HouseholdApp.Web.Pages.Expenses;

public sealed record ExpensesListViewModel(
    Guid HouseholdId,
    Guid? GroupId,
    Guid CurrentUserId,
    IReadOnlyList<ExpenseTimelineEntry> Entries,
    bool HasOlder = false);

/// <summary>One page of older rows plus the cursor for the next one (null when exhausted).</summary>
public sealed record TimelinePageViewModel(ExpensesListViewModel List, string? NextCursor);

/// <summary>
/// The "Show earlier entries" button, or an empty placeholder with the same id when <see cref="Visible"/> is false.
/// A non-null <see cref="OobSwap"/> ("outerHTML" after a page loads, "morph" on SSE pushes) swaps it in place.
/// </summary>
public sealed record ShowOlderButtonViewModel(string? Cursor, bool Visible, string? OobSwap = null);
