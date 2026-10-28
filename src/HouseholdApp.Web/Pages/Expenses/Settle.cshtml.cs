using System.ComponentModel.DataAnnotations;
using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdApp.Web.Pages.Expenses;

public class SettleModel(
    ICurrentUser currentUser,
    IHouseholdGuard guard,
    IExpenseCommands expenseCommands,
    IExpenseQueries expenseQueries,
    IHouseholdQueries householdQueries,
    TimeProvider timeProvider) : HouseholdPageModel(currentUser, guard)
{
    [BindProperty(SupportsGet = true)]
    public override Guid HouseholdId { get; set; }

    [BindProperty]
    public Guid PayerId { get; set; }

    [BindProperty]
    public Guid RecipientId { get; set; }

    [BindProperty, Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    [BindProperty]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    public IReadOnlyList<HouseholdMemberDto> Members { get; private set; } = [];

    public async Task OnGetAsync(Guid? with)
    {
        await LoadMembers();
        var balances = await expenseQueries.GetBalancesAsync(HouseholdId, CurrentUserId);
        var defaults = SettleDefaults.From(balances, CurrentUserId, with, Members.Select(m => m.UserId).ToList());

        PayerId = defaults.PayerId;
        RecipientId = defaults.RecipientId;
        if (defaults.Cents is { } cents) Amount = cents / 100m;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (PayerId == RecipientId)
            ModelState.AddModelError("RecipientId", "Payer and recipient must be different.");
        if (!ModelState.IsValid)
        {
            await LoadMembers();
            return Page();
        }

        var cents = (long)(Amount * 100);
        await expenseCommands.RecordSettlementAsync(
            HouseholdId, PayerId, RecipientId, cents,
            new DateTimeOffset(Date, TimeOnly.MinValue, TimeSpan.Zero));

        TempData["Success"] = Loc["Flash.SettlementRecorded"].Value;
        return RedirectToPage("Index", new { householdId = HouseholdId });
    }

    private async Task LoadMembers()
    {
        Members = await householdQueries.GetMembersAsync(HouseholdId);
    }
}
