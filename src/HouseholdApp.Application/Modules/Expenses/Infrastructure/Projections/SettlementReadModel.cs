using HouseholdApp.Application.Modules.Expenses.Domain;
using Marten.Events.Aggregation;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

public sealed class SettlementReadModel
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public Guid? ExpenseGroupId { get; set; }
    public Guid PayerId { get; set; }
    public Guid RecipientId { get; set; }
    public long Cents { get; set; }
    public DateTimeOffset Date { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public Guid? HouseholdSettlementId { get; set; }
    public bool IsOffset { get; set; }
    public long? HouseholdSettlementCents { get; set; }

    /// <summary>The leg that stands for a whole household settle on the home timeline.</summary>
    public bool IsHouseholdLead { get; set; }
}

public partial class SettlementReadModelProjection : SingleStreamProjection<SettlementReadModel, Guid>
{
    public void Apply(SettlementRecorded e, SettlementReadModel model)
    {
        model.Id = e.SettlementId;
        model.HouseholdId = e.HouseholdId;
        model.ExpenseGroupId = e.ExpenseGroupId;
        model.PayerId = e.PayerId;
        model.RecipientId = e.RecipientId;
        model.Cents = e.Cents;
        model.Date = e.Date;
        model.RecordedAt = e.OccurredAt;
        model.HouseholdSettlementId = e.HouseholdSettlementId;
        model.IsOffset = e.IsOffset;
        model.HouseholdSettlementCents = e.HouseholdSettlementCents;
        model.IsHouseholdLead = e.HouseholdSettlementId is { } lead && lead == e.SettlementId;
    }
}
