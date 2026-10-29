using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

namespace HouseholdApp.UnitTests.Modules.Expenses.Infrastructure;

public sealed class InstallmentPlanProjectionTests
{
    private static readonly Guid PlanId = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly InstallmentPlanProjection _projection = new();

    private static InstallmentPlanCreated Created(long downPaymentCents = 0) => new(
        Guid.NewGuid(), Now, PlanId, Guid.NewGuid(), Guid.NewGuid(), "Car repair", Ana, 150000, downPaymentCents, 5,
        new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10), [new Allocation(Ana, 150000)], 1, Ana);

    [Test]
    public async Task Created_fills_the_document()
    {
        var doc = new InstallmentPlanDocument();
        _projection.Apply(Created(), doc);

        await Assert.That(doc.Id).IsEqualTo(PlanId);
        await Assert.That(doc.Count).IsEqualTo(5);
        await Assert.That(doc.Status).IsEqualTo(InstallmentPlanStatus.Active);
        await Assert.That(doc.Segments.Single().FromNumber).IsEqualTo(1);
        await Assert.That(doc.CreatedAt).IsEqualTo(Now);
    }

    [Test]
    public async Task Created_copies_the_down_payment_and_starts_the_split_at_number_zero()
    {
        var doc = new InstallmentPlanDocument();
        _projection.Apply(Created(downPaymentCents: 50000), doc);

        await Assert.That(doc.DownPaymentCents).IsEqualTo(50000L);
        await Assert.That(doc.Segments.Single().FromNumber).IsEqualTo(0);
        await Assert.That(doc.ToTerms().Numbers).IsEquivalentTo(new[] { 0, 1, 2, 3, 4, 5 });
    }

    [Test]
    public async Task Updated_replaces_segments_from_its_number()
    {
        var doc = new InstallmentPlanDocument();
        _projection.Apply(Created(), doc);
        _projection.Apply(new InstallmentPlanUpdated(Guid.NewGuid(), Now, PlanId, doc.HouseholdId, doc.ExpenseGroupId,
            "Renamed", Ana, new SplitSegment(3, [new Allocation(Ana, 90000)]), "Car repair", Ana), doc);

        await Assert.That(doc.Description).IsEqualTo("Renamed");
        await Assert.That(doc.Segments.Select(s => s.FromNumber)).IsEquivalentTo(new[] { 1, 3 });
    }

    [Test]
    public async Task Ending_events_set_status_and_ended_at()
    {
        var doc = new InstallmentPlanDocument();
        _projection.Apply(Created(), doc);
        _projection.Apply(new InstallmentPlanPaidEarly(Guid.NewGuid(), Now, PlanId, doc.HouseholdId,
            doc.ExpenseGroupId, "Car repair", Guid.NewGuid(), 3, 5, 90000, 85000, Ana, [new Allocation(Ana, 85000)], Ana), doc);

        await Assert.That(doc.Status).IsEqualTo(InstallmentPlanStatus.PaidEarly);
        await Assert.That(doc.EndedAt).IsEqualTo(Now);
        await Assert.That(doc.PayoffCents).IsEqualTo(85000L);
    }
}
