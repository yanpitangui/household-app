using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

namespace HouseholdApp.Application.Modules.Expenses.Application.Operations;

public static class InstallmentPlanSummaries
{
    /// <summary>
    /// Status per installment number from the plan's Expenses: a live Expense means created (an edit's replacement
    /// counts), only voided ones mean voided, none means still to come (or never created once the plan ended).
    /// </summary>
    public static InstallmentPlanSummary Build(InstallmentPlanDocument plan, IReadOnlyList<ExpenseReadModel> expenses)
    {
        var byNumber = expenses
            .Where(e => e.Installment is { IsPayoff: false })
            .GroupBy(e => e.Installment!.Number)
            .ToDictionary(g => g.Key, g => g.ToList());
        var active = plan.Status == InstallmentPlanStatus.Active;

        var slots = Enumerable.Range(1, plan.Count).Select(n =>
        {
            var status = byNumber.TryGetValue(n, out var list)
                ? list.Any(e => !e.IsVoided) ? InstallmentSlotStatus.Created : InstallmentSlotStatus.Voided
                : active ? InstallmentSlotStatus.Upcoming : InstallmentSlotStatus.NotCreated;
            var cents = status == InstallmentSlotStatus.Created
                ? list!.First(e => !e.IsVoided).TotalCents
                : InstallmentSchedule.Amount(plan.TotalCents, plan.Count, n);
            return new InstallmentSlot(n, InstallmentSchedule.DueDate(plan.FirstDate, n), cents, status);
        }).ToList();

        return new InstallmentPlanSummary(
            plan.Id, plan.Description, plan.Count, plan.Status, slots,
            slots.Where(s => s.Status == InstallmentSlotStatus.Created).Sum(s => s.Cents),
            slots.Where(s => s.Status == InstallmentSlotStatus.Upcoming).Sum(s => s.Cents));
    }
}
