using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Expenses.Domain;

public sealed class Expense : AggregateRoot
{
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid ExpenseGroupId { get; private set; }
    public string Description { get; private set; } = default!;
    public DateTimeOffset Date { get; private set; }
    public IReadOnlyList<FundingSource> FundingSources { get; private set; } = [];
    public IReadOnlyList<Allocation> Allocations { get; private set; } = [];
    public bool IsVoided { get; private set; }

    private Expense() { }

    public static Expense Record(
        Guid householdId,
        Guid expenseGroupId,
        string description,
        DateTimeOffset date,
        IReadOnlyList<FundingSource> fundingSources,
        IReadOnlyList<Allocation> allocations,
        DateTimeOffset now,
        Guid performedByUserId,
        Guid? id = null,
        Guid? recurringExpenseId = null,
        Guid? correctedFromExpenseId = null)
    {
        Validate(fundingSources, allocations);

        var expense = new Expense
        {
            Id = id ?? Guid.CreateVersion7(),
            HouseholdId = householdId,
            ExpenseGroupId = expenseGroupId,
            Description = description,
            Date = date,
            FundingSources = fundingSources,
            Allocations = allocations
        };

        expense.Raise(new ExpenseRecorded(
            Guid.CreateVersion7(), now, expense.Id, householdId, expenseGroupId,
            description, date, fundingSources, allocations, performedByUserId,
            recurringExpenseId, correctedFromExpenseId));

        return expense;
    }

    public void Void(string? reason, DateTimeOffset now, Guid performedByUserId, Guid? correctedByExpenseId = null)
    {
        if (IsVoided) throw new InvalidOperationException("Expense is already voided.");
        IsVoided = true;
        Raise(new ExpenseVoided(
            Guid.CreateVersion7(), now, Id, HouseholdId, reason, FundingSources, Allocations,
            performedByUserId, Description, correctedByExpenseId));
    }

    public static Expense Create(ExpenseRecorded e) => new()
    {
        Id = e.ExpenseId,
        HouseholdId = e.HouseholdId,
        ExpenseGroupId = e.ExpenseGroupId,
        Description = e.Description,
        Date = e.Date,
        FundingSources = e.FundingSources,
        Allocations = e.Allocations
    };

    public void Apply(ExpenseVoided e) => IsVoided = true;

    private static void Validate(IReadOnlyList<FundingSource> funding, IReadOnlyList<Allocation> allocations)
    {
        var totalFunded = funding.Sum(f => f.Cents);
        var totalAllocated = allocations.Sum(a => a.Cents);
        if (totalFunded != totalAllocated)
            throw new InvalidOperationException(
                $"Sum of funding sources ({totalFunded}) must equal sum of allocations ({totalAllocated}).");
        if (totalFunded <= 0)
            throw new InvalidOperationException("Expense amount must be positive.");
    }
}
