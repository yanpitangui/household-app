using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using TUnit.Assertions.Enums;

namespace HouseholdApp.UnitTests.Modules.Expenses.Application;

public sealed class InstallmentPlanSummariesTests
{
    private static readonly Guid Ana = Guid.NewGuid();

    private static InstallmentPlanDocument Plan(InstallmentPlanStatus status = InstallmentPlanStatus.Active) => new()
    {
        Id = Guid.NewGuid(), Description = "Car repair", PayerId = Ana, TotalCents = 150000, Count = 5,
        FirstDate = new DateOnly(2026, 10, 10), Status = status,
        Segments = [new SplitSegment(1, [new Allocation(Ana, 150000)])]
    };

    private static ExpenseReadModel Installment(InstallmentPlanDocument plan, int number, bool voided = false) => new()
    {
        Id = Guid.NewGuid(), TotalCents = 30000, IsVoided = voided,
        InstallmentPlanId = plan.Id, Installment = new InstallmentLink(plan.Id, number, plan.Count)
    };

    [Test]
    public async Task Build_marks_created_voided_and_upcoming_slots()
    {
        var plan = Plan();
        var summary = InstallmentPlanSummaries.Build(plan, [Installment(plan, 1), Installment(plan, 2, voided: true)]);

        await Assert.That(summary.Slots.Select(s => s.Status)).IsEquivalentTo(new[]
        {
            InstallmentSlotStatus.Created, InstallmentSlotStatus.Voided, InstallmentSlotStatus.Upcoming,
            InstallmentSlotStatus.Upcoming, InstallmentSlotStatus.Upcoming
        }, CollectionOrdering.Matching);
        await Assert.That(summary.CreatedCents).IsEqualTo(30000L);
        await Assert.That(summary.RemainingCents).IsEqualTo(90000L);
        await Assert.That(summary.Slots[2].DueDate).IsEqualTo(new DateOnly(2026, 12, 10));
    }

    [Test]
    public async Task A_down_payment_is_slot_zero_counted_as_created_and_never_as_remaining()
    {
        var plan = Plan();
        plan.TotalCents = 150000;
        plan.DownPaymentCents = 50000;
        plan.PurchaseDate = new DateOnly(2026, 10, 1);
        var down = Installment(plan, 0);
        down.TotalCents = 50000;

        var summary = InstallmentPlanSummaries.Build(plan, [down]);

        await Assert.That(summary.Slots.Count).IsEqualTo(6);
        await Assert.That(summary.Slots[0].Number).IsEqualTo(0);
        await Assert.That(summary.Slots[0].Status).IsEqualTo(InstallmentSlotStatus.Created);
        await Assert.That(summary.Slots[0].DueDate).IsEqualTo(new DateOnly(2026, 10, 1));
        await Assert.That(summary.DownPaymentCents).IsEqualTo(50000L);
        await Assert.That(summary.CreatedCents).IsEqualTo(50000L);
        await Assert.That(summary.RemainingCents).IsEqualTo(100000L);
    }

    [Test]
    public async Task An_edited_installment_counts_once_as_created()
    {
        var plan = Plan();
        var summary = InstallmentPlanSummaries.Build(plan,
            [Installment(plan, 1, voided: true), Installment(plan, 1)]);

        await Assert.That(summary.Slots[0].Status).IsEqualTo(InstallmentSlotStatus.Created);
        await Assert.That(summary.CreatedCents).IsEqualTo(30000L);
    }

    [Test]
    public async Task Created_cents_use_the_live_expense_total_not_the_schedule_amount()
    {
        var plan = Plan();
        var edited = Installment(plan, 1);
        edited.TotalCents = 35000;

        var summary = InstallmentPlanSummaries.Build(plan, [Installment(plan, 1, voided: true), edited]);

        await Assert.That(summary.Slots[0].Cents).IsEqualTo(35000L);
        await Assert.That(summary.CreatedCents).IsEqualTo(35000L);
    }

    [Test]
    public async Task Ended_plans_show_missing_slots_as_not_created_and_nothing_remaining()
    {
        var plan = Plan(InstallmentPlanStatus.Cancelled);
        var summary = InstallmentPlanSummaries.Build(plan, [Installment(plan, 1)]);

        await Assert.That(summary.Slots[1].Status).IsEqualTo(InstallmentSlotStatus.NotCreated);
        await Assert.That(summary.RemainingCents).IsEqualTo(0L);
    }
}
