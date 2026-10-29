using HouseholdApp.Application.Modules.Expenses.Domain;
using Marten.Events.Aggregation;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

/// <summary>Read side of an Installment Plan: the plan page, Upcoming and the daily sweep read it.</summary>
public sealed class InstallmentPlanDocument
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public Guid ExpenseGroupId { get; set; }
    public string Description { get; set; } = default!;
    public Guid PayerId { get; set; }
    public long TotalCents { get; set; }
    public long DownPaymentCents { get; set; }
    public int Count { get; set; }
    public DateOnly PurchaseDate { get; set; }
    public DateOnly FirstDate { get; set; }
    public List<SplitSegment> Segments { get; set; } = [];
    public InstallmentPlanStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public Guid? PayoffExpenseId { get; set; }
    public long? PayoffCents { get; set; }

    // A method so that the stored JSON holds only the plan's own fields.
    public InstallmentTerms ToTerms() => new(TotalCents, Count, DownPaymentCents, PurchaseDate, FirstDate);
}

public partial class InstallmentPlanProjection : SingleStreamProjection<InstallmentPlanDocument, Guid>
{
    public void Apply(InstallmentPlanCreated e, InstallmentPlanDocument doc)
    {
        doc.Id = e.PlanId;
        doc.HouseholdId = e.HouseholdId;
        doc.ExpenseGroupId = e.ExpenseGroupId;
        doc.Description = e.Description;
        doc.PayerId = e.PayerId;
        doc.TotalCents = e.TotalCents;
        doc.DownPaymentCents = e.DownPaymentCents;
        doc.Count = e.Count;
        doc.PurchaseDate = e.PurchaseDate;
        doc.FirstDate = e.FirstDate;
        doc.Segments = [new SplitSegment(InstallmentTerms.FirstNumberFor(e.DownPaymentCents), e.Allocations)];
        doc.Status = InstallmentPlanStatus.Active;
        doc.CreatedAt = e.OccurredAt;
    }

    public void Apply(InstallmentPlanUpdated e, InstallmentPlanDocument doc)
    {
        doc.Description = e.Description;
        doc.PayerId = e.PayerId;
        doc.Segments = doc.Segments.Where(s => s.FromNumber < e.Segment.FromNumber).Append(e.Segment).ToList();
    }

    public void Apply(InstallmentPlanPaidEarly e, InstallmentPlanDocument doc)
    {
        doc.Status = InstallmentPlanStatus.PaidEarly;
        doc.EndedAt = e.OccurredAt;
        doc.PayoffExpenseId = e.PayoffExpenseId;
        doc.PayoffCents = e.PayoffCents;
    }

    public void Apply(InstallmentPlanCancelled e, InstallmentPlanDocument doc)
    {
        doc.Status = InstallmentPlanStatus.Cancelled;
        doc.EndedAt = e.OccurredAt;
    }

    public void Apply(InstallmentPlanCompleted e, InstallmentPlanDocument doc)
    {
        doc.Status = InstallmentPlanStatus.Completed;
        doc.EndedAt = e.OccurredAt;
    }
}
