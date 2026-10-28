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
    }
}
