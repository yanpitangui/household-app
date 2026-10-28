using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

namespace HouseholdApp.UnitTests.Modules.Expenses.Infrastructure;

public sealed class SettlementReadModelProjectionTests
{
    private readonly SettlementReadModelProjection _projection = new();

    [Test]
    public async Task Apply_SettlementRecorded_sets_all_fields()
    {
        var settlementId = Guid.NewGuid();
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var payer = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero);
        var date = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        var model = new SettlementReadModel();

        _projection.Apply(new SettlementRecorded(
            Guid.NewGuid(), occurredAt, settlementId, householdId, payer, recipient, 50000, date,
            PerformedByUserId: payer, ExpenseGroupId: groupId), model);

        await Assert.That(model.Id).IsEqualTo(settlementId);
        await Assert.That(model.HouseholdId).IsEqualTo(householdId);
        await Assert.That(model.ExpenseGroupId).IsEqualTo(groupId);
        await Assert.That(model.PayerId).IsEqualTo(payer);
        await Assert.That(model.RecipientId).IsEqualTo(recipient);
        await Assert.That(model.Cents).IsEqualTo(50000L);
        await Assert.That(model.Date).IsEqualTo(date);
        await Assert.That(model.RecordedAt).IsEqualTo(occurredAt);
    }

    [Test]
    public async Task Apply_SettlementRecorded_without_group_keeps_group_null()
    {
        var model = new SettlementReadModel();

        _projection.Apply(new SettlementRecorded(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), 100, DateTimeOffset.UtcNow, Guid.NewGuid()), model);

        await Assert.That(model.ExpenseGroupId).IsNull();
    }
}
