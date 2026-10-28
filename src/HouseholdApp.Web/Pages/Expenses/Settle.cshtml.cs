using System.ComponentModel.DataAnnotations;
using System.Globalization;
using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
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

    // Set when settling inside one group; absent for a household settle split across groups.
    [BindProperty(SupportsGet = true, Name = "group")]
    public Guid? GroupId { get; set; }

    [BindProperty]
    public Guid PayerId { get; set; }

    [BindProperty]
    public Guid RecipientId { get; set; }

    [BindProperty, Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    [BindProperty]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    public IReadOnlyList<HouseholdMemberDto> Members { get; private set; } = [];
    public string? GroupName { get; private set; }
    public SettleBreakdownViewModel? Breakdown { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid? with)
    {
        if (!await LoadAsync()) return NotFound();

        var balances = await expenseQueries.GetBalancesAsync(HouseholdId, CurrentUserId, GroupId);
        var defaults = SettleDefaults.From(balances, CurrentUserId, with, Members.Select(m => m.UserId).ToList());
        PayerId = defaults.PayerId;
        RecipientId = defaults.RecipientId;
        if (defaults.Cents is { } cents) Amount = cents / 100m;
        return Page();
    }

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        await LoadAsync();
        return Partial("_SettleBreakdown", await BuildBreakdownAsync());
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (PayerId == RecipientId)
            ModelState.AddModelError(nameof(RecipientId), Loc["Expenses.Settle.SamePerson"].Value);
        if (!await LoadAsync()) return NotFound();
        if (!ModelState.IsValid)
        {
            if (GroupId is null) Breakdown = await BuildBreakdownAsync();
            return Page();
        }

        var cents = ToCents(Amount);
        var date = new DateTimeOffset(Date, TimeOnly.MinValue, TimeSpan.Zero);

        if (GroupId is { } groupId)
        {
            try
            {
                await expenseCommands.RecordGroupSettlementAsync(HouseholdId, groupId, PayerId, RecipientId, cents, date);
            }
            catch (InvalidOperationException)
            {
                // The group was deleted while the form was open.
                TempData["Error"] = Loc["Expenses.Group.Gone"].Value;
                return RedirectToPage("Index", new { householdId = HouseholdId });
            }

            TempData["Success"] = Loc["Flash.SettlementRecorded"].Value;
            return RedirectToPage("Group", new { householdId = HouseholdId, groupId });
        }

        try
        {
            await expenseCommands.RecordHouseholdSettlementAsync(HouseholdId, PayerId, RecipientId, cents, date);
        }
        catch (SettlementSplitException)
        {
            // Balances moved since the preview; show the fresh breakdown and its reason.
            Breakdown = await BuildBreakdownAsync();
            ModelState.AddModelError(nameof(Amount), Breakdown.ErrorText ?? "");
            return Page();
        }

        TempData["Success"] = Loc["Flash.SettlementRecorded"].Value;
        return RedirectToPage("Index", new { householdId = HouseholdId });
    }

    // False when a requested group is missing or deleted.
    private async Task<bool> LoadAsync()
    {
        Members = await householdQueries.GetMembersAsync(HouseholdId);
        if (GroupId is not { } groupId) return true;

        GroupName = (await expenseQueries.ListExpenseGroupsAsync(HouseholdId)).FirstOrDefault(g => g.Id == groupId)?.Name;
        return GroupName is not null;
    }

    private async Task<SettleBreakdownViewModel> BuildBreakdownAsync()
    {
        var preview = await expenseQueries.PreviewHouseholdSettlementAsync(HouseholdId, PayerId, RecipientId, ToCents(Amount));
        var payer = FirstName(PayerId);
        var recipient = FirstName(RecipientId);
        var errorText = preview.Error is { } error
            ? Loc[$"Expenses.Settle.Error.{error}", payer, recipient, Money(preview.OwedCents)].Value
            : null;
        return new SettleBreakdownViewModel(preview, payer, recipient, errorText);
    }

    private string FirstName(Guid userId) =>
        NameFormat.First(Members.FirstOrDefault(m => m.UserId == userId)?.DisplayName ?? "?");

    private static long ToCents(decimal amount) => (long)Math.Round(amount * 100);

    private static string Money(long cents) =>
        $"{CultureInfo.CurrentCulture.NumberFormat.CurrencySymbol} {(cents / 100m).ToString("F2", CultureInfo.CurrentCulture)}";
}
