namespace HouseholdApp.Application.Modules.Expenses.Application.Ports;

public abstract record ExpenseTimelineEntry(DateTimeOffset Date);

public sealed record ExpenseEntry(ExpenseListItem Expense) : ExpenseTimelineEntry(Expense.Date);

public sealed record PaymentEntry(SettlementListItem Payment) : ExpenseTimelineEntry(Payment.Date);

public static class ExpenseTimeline
{
    // Payments sort above expenses on the same UTC day so a settle-up reads as following the expenses it clears.
    public static IReadOnlyList<ExpenseTimelineEntry> Merge(
        IEnumerable<ExpenseListItem> expenses, IEnumerable<SettlementListItem> payments) =>
        expenses.Select(e => (ExpenseTimelineEntry)new ExpenseEntry(e))
            .Concat(payments.Select(p => new PaymentEntry(p)))
            .OrderByDescending(x => DateOnly.FromDateTime(x.Date.UtcDateTime))
            .ThenBy(x => x is PaymentEntry ? 0 : 1)
            .ThenByDescending(x => x.Date)
            .ToList();
}
