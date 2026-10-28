using HouseholdApp.Application.Modules.Expenses.Application.Ports;

namespace HouseholdApp.Web.Pages.Expenses;

/// <summary>
/// OutOfBand is set only for SSE pushes, so the card in a full page render (including
/// htmx-boosted navigation) is part of the page rather than an out-of-band swap.
/// GroupId/GroupName scope the card to one group: the eyebrow names it and settle links stay inside it.
/// </summary>
public sealed record BalanceCardViewModel(
    Guid HouseholdId,
    IReadOnlyList<MemberBalance> Balances,
    bool OutOfBand,
    Guid? GroupId = null,
    string? GroupName = null);
