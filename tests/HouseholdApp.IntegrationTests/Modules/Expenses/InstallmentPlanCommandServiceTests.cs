using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.IntegrationTests.Infrastructure;
using HouseholdApp.Application.Modules.Expenses.Infrastructure;
using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Modules.Identity.Application.Ports;
using Marten;
using Marten.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

// Own schema + serial: the sweep reads every active plan in its schema.
[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(nameof(InstallmentPlanCommandServiceTests))]
public sealed class InstallmentPlanCommandServiceTests(PostgresFixture db)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 10);
    private readonly Guid _householdId = Guid.NewGuid();
    private readonly Guid _groupId = Guid.NewGuid();
    private readonly Guid _ana = Guid.NewGuid();
    private readonly Guid _leo = Guid.NewGuid();

    private IDocumentStore Store => ExpenseDocumentStore.For(db.ConnectionString, "expenses_installment_sweep_test");

    private InstallmentPlanCommandService BuildSut(
        IDocumentSession session, DateTimeOffset? now = null, IEventBus? bus = null, int timeoutSeconds = 10)
    {
        bus ??= new RecordingBus();
        return new(
            session, new FakeCurrentUser(_ana), new FakeTimeProvider(now ?? Now), bus,
            new TestSessionFactory(Store, bus, timeoutSeconds), NullLogger<InstallmentPlanCommandService>.Instance);
    }

    private async Task<Guid> CreatePlanAsync(
        DateOnly firstDate, long total = 150000, int count = 5, DateTimeOffset? createdAt = null,
        long down = 0, DateOnly? purchaseDate = null)
    {
        await ExpenseTestData.SeedGroupAsync(Store, _householdId, _groupId);
        await using var session = Store.LightweightSession();
        return await BuildSut(session, createdAt).CreateAsync(
            _householdId, _groupId, "Car repair", _ana, total, down, count, purchaseDate ?? firstDate, firstDate,
            [new AllocationDto(_ana, total / 2), new AllocationDto(_leo, total - total / 2)]);
    }

    private async Task<List<ExpenseReadModel>> InstallmentsAsync(Guid planId)
    {
        await using var qs = Store.QuerySession();
        return (await qs.Query<ExpenseReadModel>().Where(e => e.InstallmentPlanId == planId).ToListAsync()).ToList();
    }

    [Test]
    public async Task Create_records_past_due_installments_in_the_same_transaction()
    {
        var planId = await CreatePlanAsync(firstDate: new DateOnly(2026, 9, 10)); // Sep, Oct, Nov due

        var installments = await InstallmentsAsync(planId);
        await Assert.That(installments.Select(e => e.Installment!.Number).Order()).IsEquivalentTo(new[] { 1, 2, 3 });
        await Assert.That(installments.All(e => e.TotalCents == 30000)).IsTrue();

        await using var qs = Store.QuerySession();
        var plan = await qs.LoadAsync<InstallmentPlanDocument>(planId);
        await Assert.That(plan!.Status).IsEqualTo(InstallmentPlanStatus.Active);
        var ledger = await qs.LoadAsync<HouseholdLedger>(_householdId);
        var leoOwes = ledger!.Pairs.Where(p => p.GroupId == _groupId)
            .Sum(p => p.UserId1 == _ana ? p.Cents : -p.Cents);
        await Assert.That(leoOwes).IsEqualTo(45000L);
    }

    [Test]
    public async Task Create_with_a_future_first_date_records_nothing_yet()
    {
        var planId = await CreatePlanAsync(firstDate: new DateOnly(2026, 12, 10));
        await Assert.That((await InstallmentsAsync(planId)).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Create_rejects_a_deleted_group()
    {
        await ExpenseTestData.SeedGroupAsync(Store, _householdId, _groupId);
        await SetGroupDeletedAsync(true);

        await using var session = Store.LightweightSession();
        await Assert.That(() => BuildSut(session).CreateAsync(
            _householdId, _groupId, "X", _ana, 1000, 0, 2, Today, Today, [new AllocationDto(_ana, 1000)]))
            .Throws<InvalidOperationException>();
    }

    private async Task SweepAsync(DateOnly today)
    {
        await using var session = Store.LightweightSession();
        await BuildSut(session, new DateTimeOffset(today, new TimeOnly(3, 0), TimeSpan.Zero)).RunSweepAsync(today);
    }

    [Test]
    public async Task Sweep_creates_installments_as_they_fall_due_and_reruns_create_nothing()
    {
        var planId = await CreatePlanAsync(firstDate: new DateOnly(2026, 11, 10)); // #1 at creation

        await SweepAsync(new DateOnly(2026, 12, 10));
        await SweepAsync(new DateOnly(2026, 12, 10));

        var numbers = (await InstallmentsAsync(planId)).Select(e => e.Installment!.Number).Order().ToList();
        await Assert.That(numbers).IsEquivalentTo(new[] { 1, 2 });
    }

    [Test]
    public async Task Create_completes_the_plan_when_every_installment_is_already_due()
    {
        var planId = await CreatePlanAsync(firstDate: new DateOnly(2026, 10, 10), total: 20000, count: 2);

        await using var qs = Store.QuerySession();
        var plan = await qs.LoadAsync<InstallmentPlanDocument>(planId);
        await Assert.That(plan!.Status).IsEqualTo(InstallmentPlanStatus.Completed);
        await Assert.That((await InstallmentsAsync(planId)).Count).IsEqualTo(2);
    }

    [Test]
    public async Task Sweep_marks_the_plan_completed_once_every_installment_exists()
    {
        var planId = await CreatePlanAsync(firstDate: new DateOnly(2026, 10, 10), total: 30000, count: 3);

        await SweepAsync(new DateOnly(2026, 12, 10));

        await using var qs = Store.QuerySession();
        var plan = await qs.LoadAsync<InstallmentPlanDocument>(planId);
        await Assert.That(plan!.Status).IsEqualTo(InstallmentPlanStatus.Completed);
    }

    [Test]
    public async Task Sweep_skips_deleted_groups_and_catches_up_after_restore()
    {
        var planId = await CreatePlanAsync(firstDate: new DateOnly(2026, 12, 10));
        await SetGroupDeletedAsync(true);
        await SweepAsync(new DateOnly(2027, 1, 10));
        await Assert.That((await InstallmentsAsync(planId)).Count).IsEqualTo(0);

        await SetGroupDeletedAsync(false);
        await SweepAsync(new DateOnly(2027, 1, 11));

        var installments = await InstallmentsAsync(planId);
        await Assert.That(installments.Select(e => e.Date.Month).Order()).IsEquivalentTo(new[] { 1, 12 });
    }

    [Test]
    public async Task Sweep_does_not_recreate_a_voided_installment()
    {
        var planId = await CreatePlanAsync(firstDate: Today);
        var first = InstallmentSchedule.InstallmentExpenseId(planId, 1);
        await using (var s = Store.LightweightSession())
        {
            var expense = await s.Events.AggregateStreamAsync<Expense>(first);
            expense!.Void(null, Now, _ana);
            s.Events.Append(first, expense.DomainEvents.ToArray());
            await s.SaveChangesAsync();
        }

        await SweepAsync(Today);

        var installments = await InstallmentsAsync(planId);
        await Assert.That(installments.Count).IsEqualTo(1);
        await Assert.That(installments[0].IsVoided).IsTrue();
    }

    [Test]
    public async Task Sweep_isolates_a_colliding_plan_and_never_publishes_its_events()
    {
        var first = new DateOnly(2026, 12, 10);
        var planA = await CreatePlanAsync(first, total: 20000, count: 2);
        var planB = await CreatePlanAsync(first, total: 20000, count: 2);

        // A concurrent sweep already started A's installment #1 stream; its read model is not visible yet.
        var collidingId = InstallmentSchedule.InstallmentExpenseId(planA, 1);
        var existing = Expense.Record(_householdId, _groupId, "Car repair", Now,
            [new FundingSource(_ana, 10000)], [new Allocation(_ana, 10000)], Now, _ana, id: collidingId);
        await using (var s = Store.LightweightSession())
        {
            s.Events.StartStream(collidingId, existing.DomainEvents.ToArray());
            await s.SaveChangesAsync();
            s.Delete<ExpenseReadModel>(collidingId);
            await s.SaveChangesAsync();
        }

        var bus = new RecordingBus();
        var sweepDay = new DateOnly(2027, 1, 10);
        await using (var s = Store.LightweightSession(
            new SessionOptions { Listeners = { new ExpenseEventPublishingListener(bus) } }))
        {
            await BuildSut(s, new DateTimeOffset(sweepDay, new TimeOnly(3, 0), TimeSpan.Zero), bus)
                .RunSweepAsync(sweepDay);
        }

        await using var qs = Store.QuerySession();
        var docB = await qs.LoadAsync<InstallmentPlanDocument>(planB);
        await Assert.That(docB!.Status).IsEqualTo(InstallmentPlanStatus.Completed);
        await Assert.That((await InstallmentsAsync(planB)).Count).IsEqualTo(2);
        var docA = await qs.LoadAsync<InstallmentPlanDocument>(planA);
        await Assert.That(docA!.Status).IsEqualTo(InstallmentPlanStatus.Active);
        await Assert.That((await InstallmentsAsync(planA)).Count).IsEqualTo(0);

        await Assert.That(bus.Queued.Count).IsEqualTo(0);
        var publishedForA = bus.Dispatched.Count(e => e switch
        {
            ExpenseRecorded r => r.Installment?.PlanId == planA,
            InstallmentPlanCompleted c => c.PlanId == planA,
            _ => false
        });
        await Assert.That(publishedForA).IsEqualTo(0);
        await Assert.That(bus.Dispatched.OfType<InstallmentPlanCompleted>().Any(c => c.PlanId == planB)).IsTrue();
    }

    [Test]
    public async Task Update_changes_only_installments_not_yet_created()
    {
        var planId = await CreatePlanAsync(firstDate: new DateOnly(2026, 10, 10)); // #1, #2 created
        await using (var session = Store.LightweightSession())
            await BuildSut(session).UpdateAsync(_householdId, planId, "Car repair (shop)", _leo,
                [new AllocationDto(_leo, 90000)]);

        await SweepAsync(new DateOnly(2026, 12, 10));

        var installments = (await InstallmentsAsync(planId)).OrderBy(e => e.Installment!.Number).ToList();
        await Assert.That(installments[1].Description).IsEqualTo("Car repair");
        await Assert.That(installments[2].Description).IsEqualTo("Car repair (shop)");
        await Assert.That(installments[2].FundingSources.Single().UserId).IsEqualTo(_leo);
    }

    [Test]
    public async Task PayRemainingEarly_records_one_expense_and_ends_the_plan()
    {
        var planId = await CreatePlanAsync(firstDate: new DateOnly(2026, 10, 10)); // #1, #2 created, 900 remaining

        Guid payoffId;
        await using (var session = Store.LightweightSession())
            payoffId = await BuildSut(session).PayRemainingEarlyAsync(_householdId, planId, 85000, "Car repair (paid early 3-5/5)");

        await using var qs = Store.QuerySession();
        var payoff = await qs.LoadAsync<ExpenseReadModel>(payoffId);
        await Assert.That(payoff!.TotalCents).IsEqualTo(85000L);
        await Assert.That(payoff.Installment).IsEqualTo(new InstallmentLink(planId, 3, 5, 5));
        var plan = await qs.LoadAsync<InstallmentPlanDocument>(planId);
        await Assert.That(plan!.Status).IsEqualTo(InstallmentPlanStatus.PaidEarly);

        await SweepAsync(new DateOnly(2027, 3, 10));
        await Assert.That((await InstallmentsAsync(planId)).Count).IsEqualTo(3); // #1, #2, payoff
    }

    private async Task<long> LeoOwesAnaAsync()
    {
        await using var qs = Store.QuerySession();
        var ledger = await qs.LoadAsync<HouseholdLedger>(_householdId);
        return ledger!.Pairs.Where(p => p.GroupId == _groupId).Sum(p => p.UserId1 == _ana ? p.Cents : -p.Cents);
    }

    [Test]
    public async Task Create_with_a_down_payment_records_only_the_down_payment_when_the_first_installment_is_next_month()
    {
        var planId = await CreatePlanAsync(firstDate: new DateOnly(2026, 12, 10), down: 50000, purchaseDate: Today);

        var installments = await InstallmentsAsync(planId);
        var down = installments.Single();
        await Assert.That(down.Installment).IsEqualTo(new InstallmentLink(planId, 0, 5));
        await Assert.That(down.TotalCents).IsEqualTo(50000L);
        await Assert.That(await LeoOwesAnaAsync()).IsEqualTo(25000L);
    }

    [Test]
    public async Task Sweep_after_a_down_payment_creates_installment_one_and_pay_early_covers_the_rest()
    {
        var planId = await CreatePlanAsync(firstDate: new DateOnly(2026, 12, 10), down: 50000, purchaseDate: Today);

        await SweepAsync(new DateOnly(2026, 12, 10));
        var numbers = (await InstallmentsAsync(planId)).Select(e => e.Installment!.Number).Order().ToList();
        await Assert.That(numbers).IsEquivalentTo(new[] { 0, 1 });

        Guid payoffId;
        await using (var session = Store.LightweightSession())
            payoffId = await BuildSut(session).PayRemainingEarlyAsync(_householdId, planId, 80000, "Car repair (paid early)");

        await using var qs = Store.QuerySession();
        var payoff = await qs.LoadAsync<ExpenseReadModel>(payoffId);
        await Assert.That(payoff!.Installment).IsEqualTo(new InstallmentLink(planId, 2, 5, 5));
        await Assert.That(payoff.TotalCents).IsEqualTo(80000L);
    }

    [Test]
    public async Task Pay_early_right_after_creating_with_only_the_down_payment_covers_installments_one_to_n()
    {
        var planId = await CreatePlanAsync(firstDate: new DateOnly(2026, 12, 10), down: 50000, purchaseDate: Today);

        Guid payoffId;
        await using (var session = Store.LightweightSession())
            payoffId = await BuildSut(session).PayRemainingEarlyAsync(_householdId, planId, 100000, "Car repair (paid early)");

        await using var qs = Store.QuerySession();
        var payoff = await qs.LoadAsync<ExpenseReadModel>(payoffId);
        await Assert.That(payoff!.Installment).IsEqualTo(new InstallmentLink(planId, 1, 5, 5));
    }

    [Test]
    public async Task A_plan_with_a_down_payment_completes_once_numbers_zero_to_n_exist()
    {
        var planId = await CreatePlanAsync(
            firstDate: new DateOnly(2026, 12, 10), total: 30000, count: 2, down: 10000, purchaseDate: Today);

        await SweepAsync(new DateOnly(2026, 12, 10));
        await using (var qs = Store.QuerySession())
            await Assert.That((await qs.LoadAsync<InstallmentPlanDocument>(planId))!.Status)
                .IsEqualTo(InstallmentPlanStatus.Active);

        await SweepAsync(new DateOnly(2027, 1, 10));

        await using var after = Store.QuerySession();
        await Assert.That((await after.LoadAsync<InstallmentPlanDocument>(planId))!.Status)
            .IsEqualTo(InstallmentPlanStatus.Completed);
        var installments = await InstallmentsAsync(planId);
        await Assert.That(installments.Select(e => e.Installment!.Number).Order()).IsEquivalentTo(new[] { 0, 1, 2 });
        await Assert.That(installments.Sum(e => e.TotalCents)).IsEqualTo(30000L);
        await Assert.That(await LeoOwesAnaAsync()).IsEqualTo(15000L);
    }

    [Test]
    public async Task Create_completes_a_plan_with_a_down_payment_when_every_number_is_already_due()
    {
        var planId = await CreatePlanAsync(
            firstDate: new DateOnly(2026, 10, 10), total: 30000, count: 2, down: 10000,
            purchaseDate: new DateOnly(2026, 10, 10));

        await using var qs = Store.QuerySession();
        await Assert.That((await qs.LoadAsync<InstallmentPlanDocument>(planId))!.Status)
            .IsEqualTo(InstallmentPlanStatus.Completed);
        await Assert.That((await InstallmentsAsync(planId)).Count).IsEqualTo(3);
        await Assert.That(await LeoOwesAnaAsync()).IsEqualTo(15000L);
    }

    [Test]
    public async Task Cancel_with_void_reverses_the_created_installments()
    {
        var planId = await CreatePlanAsync(firstDate: new DateOnly(2026, 10, 10));
        await using (var session = Store.LightweightSession())
            await BuildSut(session).CancelAsync(_householdId, planId, voidCreated: true);

        var installments = await InstallmentsAsync(planId);
        await Assert.That(installments.All(e => e.IsVoided)).IsTrue();
        await using var qs = Store.QuerySession();
        var ledger = await qs.LoadAsync<HouseholdLedger>(_householdId);
        await Assert.That(ledger!.Pairs.Where(p => p.GroupId == _groupId).Sum(p => p.Cents)).IsEqualTo(0L);
        var plan = await qs.LoadAsync<InstallmentPlanDocument>(planId);
        await Assert.That(plan!.Status).IsEqualTo(InstallmentPlanStatus.Cancelled);
    }

    [Test]
    public async Task Cancel_without_void_keeps_created_installments()
    {
        var planId = await CreatePlanAsync(firstDate: new DateOnly(2026, 10, 10));
        await using (var session = Store.LightweightSession())
            await BuildSut(session).CancelAsync(_householdId, planId, voidCreated: false);

        await Assert.That((await InstallmentsAsync(planId)).Any(e => e.IsVoided)).IsFalse();
    }

    [Test]
    public async Task Plan_actions_reject_another_household()
    {
        var planId = await CreatePlanAsync(firstDate: Today);
        await using var session = Store.LightweightSession();
        await Assert.That(() => BuildSut(session).CancelAsync(Guid.NewGuid(), planId, false))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Concurrent_plan_actions_serialize_on_the_plan_lock()
    {
        var planId = await CreatePlanAsync(firstDate: new DateOnly(2026, 12, 10));

        Task? cancel = null;
        var cancelFinishedWhileUpdateHeldTheLock = false;
        var updateSession = Store.LightweightSession(new SessionOptions
        {
            Listeners =
            {
                new BeforeSaveListener(async () =>
                {
                    // The update holds the plan lock here; the cancel starts now and has to wait for it.
                    cancel = Task.Run(async () =>
                    {
                        await using var other = Store.LightweightSession();
                        await BuildSut(other).CancelAsync(_householdId, planId, voidCreated: false);
                    });
                    var finished = await Task.WhenAny(cancel, Task.Delay(TimeSpan.FromSeconds(1)));
                    cancelFinishedWhileUpdateHeldTheLock = finished == cancel;
                })
            }
        });
        await using (updateSession)
            await BuildSut(updateSession).UpdateAsync(_householdId, planId, "Car repair (shop)", _leo,
                [new AllocationDto(_leo, 150000)]);

        await cancel!.WaitAsync(TimeSpan.FromSeconds(30));

        await Assert.That(cancelFinishedWhileUpdateHeldTheLock).IsFalse();
        await using var qs = Store.QuerySession();
        var plan = await qs.LoadAsync<InstallmentPlanDocument>(planId);
        await Assert.That(plan!.Status).IsEqualTo(InstallmentPlanStatus.Cancelled);
        await Assert.That(plan.Description).IsEqualTo("Car repair (shop)");
    }

    [Test]
    public async Task Sweep_waits_for_a_running_pay_early_so_no_installment_is_charged_twice()
    {
        var planId = await CreatePlanAsync(firstDate: new DateOnly(2026, 10, 10)); // #1, #2 created; #3 due Dec 10
        var sweepDay = new DateOnly(2026, 12, 10);

        Task? sweep = null;
        var sweepFinishedWhilePayoffHeldTheLock = false;
        var payoffSession = Store.LightweightSession(new SessionOptions
        {
            Listeners =
            {
                new BeforeSaveListener(async () =>
                {
                    // The payoff holds the plan lock here; the sweep starts now and has to wait for it.
                    sweep = Task.Run(() => SweepAsync(sweepDay));
                    var finished = await Task.WhenAny(sweep, Task.Delay(TimeSpan.FromSeconds(1)));
                    sweepFinishedWhilePayoffHeldTheLock = finished == sweep;
                })
            }
        });
        await using (payoffSession)
            await BuildSut(payoffSession).PayRemainingEarlyAsync(_householdId, planId, 90000, "Car repair (paid early)");

        await sweep!.WaitAsync(TimeSpan.FromSeconds(30));

        await Assert.That(sweepFinishedWhilePayoffHeldTheLock).IsFalse();
        var expenses = await InstallmentsAsync(planId);
        await Assert.That(expenses.Count(e => e.Installment is { Number: 3, IsPayoff: false })).IsEqualTo(0);
        await Assert.That(expenses.Sum(e => e.TotalCents)).IsEqualTo(150000L);
        await using var qs = Store.QuerySession();
        var ledger = await qs.LoadAsync<HouseholdLedger>(_householdId);
        var leoOwes = ledger!.Pairs.Where(p => p.GroupId == _groupId)
            .Sum(p => p.UserId1 == _ana ? p.Cents : -p.Cents);
        await Assert.That(leoOwes).IsEqualTo(75000L);
    }

    [Test]
    public async Task Sweep_continues_when_another_plan_is_locked()
    {
        var first = new DateOnly(2026, 12, 10);
        var planA = await CreatePlanAsync(first, total: 20000, count: 2);
        var planB = await CreatePlanAsync(first, total: 20000, count: 2, createdAt: Now.AddMinutes(1));
        var planC = await CreatePlanAsync(first, total: 20000, count: 2, createdAt: Now.AddMinutes(2));

        // Another session holds A's lock for the whole sweep; the sweep's command timeout is short so A fails fast.
        await using var holder = Store.LightweightSession();
        await holder.Events.FetchForExclusiveWriting<InstallmentPlan>(planA);

        var bus = new RecordingBus();
        var sweepDay = new DateOnly(2027, 1, 10);
        await using (var s = Store.LightweightSession())
        {
            await BuildSut(s, new DateTimeOffset(sweepDay, new TimeOnly(3, 0), TimeSpan.Zero), bus, timeoutSeconds: 2)
                .RunSweepAsync(sweepDay);
        }

        await using var qs = Store.QuerySession();
        var docB = await qs.LoadAsync<InstallmentPlanDocument>(planB);
        await Assert.That(docB!.Status).IsEqualTo(InstallmentPlanStatus.Completed);
        await Assert.That((await InstallmentsAsync(planB)).Count).IsEqualTo(2);
        // A is swept first (oldest), so C proves the sweep carried on past the locked plan.
        var docC = await qs.LoadAsync<InstallmentPlanDocument>(planC);
        await Assert.That(docC!.Status).IsEqualTo(InstallmentPlanStatus.Completed);
        await Assert.That((await InstallmentsAsync(planC)).Count).IsEqualTo(2);
        var docA = await qs.LoadAsync<InstallmentPlanDocument>(planA);
        await Assert.That(docA!.Status).IsEqualTo(InstallmentPlanStatus.Active);
        await Assert.That((await InstallmentsAsync(planA)).Count).IsEqualTo(0);

        await Assert.That(bus.Queued.Count).IsEqualTo(0);
        await Assert.That(bus.Dispatched.Count(e => e is ExpenseRecorded { Installment: { } l } && l.PlanId == planA))
            .IsEqualTo(0);
    }

    [Test]
    public async Task Sweep_leaves_a_skipped_plan_unlocked()
    {
        var planId = await CreatePlanAsync(new DateOnly(2026, 12, 10));
        await SetGroupDeletedAsync(true);

        var sweepDay = new DateOnly(2027, 1, 10);
        await using var s = Store.LightweightSession();
        await BuildSut(s, new DateTimeOffset(sweepDay, new TimeOnly(3, 0), TimeSpan.Zero)).RunSweepAsync(sweepDay);

        // Still inside the sweep's scope: the plan is free as soon as its own sweep ends.
        await using var other = Store.LightweightSession(new SessionOptions { Timeout = 2 });
        var stream = await other.Events.FetchForExclusiveWriting<InstallmentPlan>(planId);
        await Assert.That(stream.Aggregate).IsNotNull();
    }

    [Test]
    public async Task Plan_history_lists_plan_rows_and_hides_covered_expenses()
    {
        var planId = await CreatePlanAsync(firstDate: new DateOnly(2026, 10, 10)); // 2 catch-up, hidden
        await SweepAsync(new DateOnly(2026, 12, 10));                               // #3 shown

        await using var qs = Store.QuerySession();
        var kinds = (await qs.Query<ActivityEntry>().Where(a => a.InstallmentPlanId == planId).ToListAsync())
            .Select(a => a.Kind).ToList();

        await Assert.That(kinds).IsEquivalentTo(new[] { ActivityKind.InstallmentPlanCreated, ActivityKind.InstallmentAdded });

        var users = Substitute.For<IUserQuery>();
        users.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<UserProfile>)[new UserProfile(_ana, "sub-ana", "ana@x.com", "Ana", null)]);
        var feed = new ActivityFeedQueryService(qs, users);

        var history = await feed.GetPlanHistoryAsync(_householdId, planId, _leo);
        await Assert.That(history.Select(h => h.Kind)).IsEquivalentTo(
            new[] { ActivityKind.InstallmentAdded, ActivityKind.InstallmentPlanCreated });
        await Assert.That(history[0].Kind).IsEqualTo(ActivityKind.InstallmentAdded); // newest first
        await Assert.That(history[0].InstallmentNumber).IsEqualTo(3);
        await Assert.That((await feed.GetPlanHistoryAsync(Guid.NewGuid(), planId, _leo)).Count).IsEqualTo(0);
    }

    private async Task SetGroupDeletedAsync(bool deleted)
    {
        await using var s = Store.LightweightSession();
        var group = await s.LoadAsync<ExpenseGroupDocument>(_groupId);
        group!.DeletedAt = deleted ? Now : null;
        s.Store(group);
        await s.SaveChangesAsync();
    }
}

file sealed class TestSessionFactory(IDocumentStore store, IEventBus bus, int timeoutSeconds) : ISessionFactory
{
    public IQuerySession QuerySession() => store.QuerySession();

    public IDocumentSession OpenSession() => store.LightweightSession(new SessionOptions
    {
        Timeout = timeoutSeconds,
        Listeners = { new ExpenseEventPublishingListener(bus) }
    });
}

file sealed class BeforeSaveListener(Func<Task> action) : DocumentSessionListenerBase
{
    public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token) => action();
}

file sealed class RecordingBus : IEventBus
{
    public Queue<IDomainEvent> Queued { get; } = new();
    public List<IDomainEvent> Dispatched { get; } = [];

    public void Enqueue(IDomainEvent @event) => Queued.Enqueue(@event);
    public void Discard() => Queued.Clear();
    public Task FlushTransactionalAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task FlushDeferredAsync(CancellationToken ct = default)
    {
        while (Queued.TryDequeue(out var e)) Dispatched.Add(e);
        return Task.CompletedTask;
    }
}
