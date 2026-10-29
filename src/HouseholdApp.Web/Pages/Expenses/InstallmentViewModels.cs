using HouseholdApp.Application.Modules.Expenses.Application.Ports;

namespace HouseholdApp.Web.Pages.Expenses;

/// <summary>
/// The installment list. <see cref="Highlight"/> marks one number ("this" in the timeline, "latest" on the plan page);
/// <see cref="Compact"/> folds long plans to previous / highlighted / next.
/// </summary>
public sealed record InstallmentPlanSectionViewModel(
    Guid HouseholdId, InstallmentPlanSummary Plan, int? Highlight, string HighlightTagKey, bool Compact, bool ShowFooter);

/// <summary>The Upcoming strip: next-30-days installments and recurring expenses not yet in the balance.</summary>
public sealed record UpcomingStripViewModel(Guid HouseholdId, Guid CurrentUserId, UpcomingSummary Upcoming);
