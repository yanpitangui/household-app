using HouseholdApp.Application.Modules.Expenses.Domain;
using Marten.Events.Aggregation;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

public sealed class ExpenseReadModel
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public Guid ExpenseGroupId { get; set; }
    public string Description { get; set; } = default!;
    public DateTimeOffset Date { get; set; }
    public long TotalCents { get; set; }
    public bool IsVoided { get; set; }
    public string? VoidReason { get; set; }
    public IReadOnlyList<FundingSource> FundingSources { get; set; } = [];
    public IReadOnlyList<Allocation> Allocations { get; set; } = [];
    public DateTimeOffset RecordedAt { get; set; }
}

public partial class ExpenseReadModelProjection : SingleStreamProjection<ExpenseReadModel, Guid>
{
    public void Apply(ExpenseRecorded e, ExpenseReadModel model)
    {
        model.Id = e.ExpenseId;
        model.HouseholdId = e.HouseholdId;
        model.ExpenseGroupId = e.ExpenseGroupId;
        model.Description = e.Description;
        model.Date = e.Date;
        model.TotalCents = e.FundingSources.Sum(f => f.Cents);
        model.FundingSources = e.FundingSources;
        model.Allocations = e.Allocations;
        model.RecordedAt = e.OccurredAt;
    }

    public void Apply(ExpenseVoided e, ExpenseReadModel model)
    {
        model.IsVoided = true;
        model.VoidReason = e.Reason;
    }
}
