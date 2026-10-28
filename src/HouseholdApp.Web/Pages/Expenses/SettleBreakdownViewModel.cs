using HouseholdApp.Application.Modules.Expenses.Application.Ports;

namespace HouseholdApp.Web.Pages.Expenses;

public sealed record SettleBreakdownViewModel(
    SettlementPreview Preview, string PayerName, string RecipientName, string? ErrorText);
