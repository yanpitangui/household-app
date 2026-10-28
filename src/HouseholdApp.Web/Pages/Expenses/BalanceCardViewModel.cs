using HouseholdApp.Application.Modules.Expenses.Application.Ports;

namespace HouseholdApp.Web.Pages.Expenses;

/// <summary>
/// OutOfBand is set only for SSE pushes, so the card in a full page render (including
/// htmx-boosted navigation) is part of the page rather than an out-of-band swap.
/// </summary>
public sealed record BalanceCardViewModel(
    IReadOnlyList<MemberBalance> Balances,
    bool OutOfBand);
