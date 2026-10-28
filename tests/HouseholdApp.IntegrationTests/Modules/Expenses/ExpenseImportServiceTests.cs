using HouseholdApp.Application.Modules.Expenses.Application.Import;
using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Modules.Identity.Application.Ports;
using HouseholdApp.Application.Modules.Notifications.Application.Ports;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.IntegrationTests.Infrastructure;
using Marten;
using Marten.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class ExpenseImportServiceTests(PostgresFixture db)
{
    private const string Casa = "casa-ficticia_2026-01-31_export.csv";
    private const string Viagem = "viagem-ficticia_2026-02-28_export.csv";

    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Bruno = Guid.NewGuid();

    private IDocumentStore Store => ExpenseDocumentStore.For(db.ConnectionString);

    private static ImportPlan PlanFor(string fixture)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Splitwise", fixture));
        return SplitwiseImportPlanner.Build(SplitwiseCsvParser.Parse(text).File!).Plan!;
    }

    // Wires the real event bus, Marten publishing listener and push handler so the test covers
    // the same dispatch path the app uses.
    private async Task<(Guid GroupId, IPushSender Push)> ImportAsync(
        Guid householdId, string groupName, ImportPlan plan,
        IReadOnlyList<Guid>? mapping = null, Action<IServiceCollection>? configure = null, IPushSender? push = null)
    {
        push ??= Substitute.For<IPushSender>();
        var households = Substitute.For<IHouseholdQueries>();
        households.GetMembersAsync(householdId, Arg.Any<CancellationToken>()).Returns(
        [
            new HouseholdMemberDto(Ana, "Ana Lima", "Owner", null),
            new HouseholdMemberDto(Bruno, "Bruno Costa", "Member", null)
        ]);

        var services = new ServiceCollection();
        services.AddEventBus();
        services.AddSingleton(push);
        services.AddSingleton(households);
        services.AddSingleton(Substitute.For<IUserQuery>());
        services.AddEventHandler<ExpenseRecorded, ExpensePushNotificationHandler>();
        services.AddEventHandler<SettlementRecorded, ExpensePushNotificationHandler>();
        configure?.Invoke(services);
        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();

        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        await using var session = Store.LightweightSession(new SessionOptions
        {
            Listeners = { new ExpenseEventPublishingListener(bus) }
        });
        var sut = new ExpenseImportService(
            session, households, scope.ServiceProvider.GetRequiredService<EventDispatchScope>(), new FakeTimeProvider());

        var groupId = await sut.ImportAsync(householdId, groupName, plan, mapping ?? [Ana, Bruno]);
        return (groupId, push);
    }

    private async Task<Dictionary<Guid, long>> NetPerMemberAsync(Guid householdId)
    {
        await using var qs = Store.QuerySession();
        var ledger = await qs.LoadAsync<HouseholdLedger>(householdId);
        var net = new Dictionary<Guid, long>();
        foreach (var p in ledger?.Pairs ?? [])
        {
            net[p.UserId1] = net.GetValueOrDefault(p.UserId1) + p.Cents;
            net[p.UserId2] = net.GetValueOrDefault(p.UserId2) - p.Cents;
        }
        return net;
    }

    /// <summary>
    /// Local confidence check against real Splitwise exports kept outside the repo:
    /// SPLITWISE_EXPORTS_DIR=&lt;dir&gt; dotnet test tests/HouseholdApp.IntegrationTests
    ///   --treenode-filter "/*/*/ExpenseImportServiceTests/Real_exports_reach_the_ledger_with_the_Splitwise_total"
    /// </summary>
    [Test]
    [Explicit]
    public async Task Real_exports_reach_the_ledger_with_the_Splitwise_total()
    {
        var dir = Environment.GetEnvironmentVariable("SPLITWISE_EXPORTS_DIR");
        await Assert.That(dir).IsNotNull();

        var files = Directory.GetFiles(dir!, "*_export*.csv");
        await Assert.That(files.Length).IsGreaterThan(0);

        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            var parsed = SplitwiseCsvParser.Parse(await File.ReadAllTextAsync(path));
            await Assert.That(parsed.Errors.Count).IsEqualTo(0).Because(name);
            var planned = SplitwiseImportPlanner.Build(parsed.File!);
            await Assert.That(planned.Errors.Count).IsEqualTo(0).Because(name);
            var plan = planned.Plan!;

            var householdId = Guid.NewGuid();
            await ImportAsync(householdId, "Real", plan);

            var net = await NetPerMemberAsync(householdId);
            await Assert.That(net.GetValueOrDefault(Ana)).IsEqualTo(plan.ReportedNetCents[0]).Because(name);
            await Assert.That(net.GetValueOrDefault(Bruno)).IsEqualTo(plan.ReportedNetCents[1]).Because(name);
        }
    }

    [Test]
    public async Task Import_reproduces_the_Splitwise_balance_in_the_ledger()
    {
        var householdId = Guid.NewGuid();

        await ImportAsync(householdId, "Casa", PlanFor(Casa));

        var net = await NetPerMemberAsync(householdId);
        await Assert.That(net[Ana]).IsEqualTo(-65148L);
        await Assert.That(net[Bruno]).IsEqualTo(65148L);
    }

    [Test]
    public async Task Importing_two_files_into_one_household_adds_their_balances()
    {
        var householdId = Guid.NewGuid();

        await ImportAsync(householdId, "Casa", PlanFor(Casa));
        await ImportAsync(householdId, "Viagem", PlanFor(Viagem));

        var net = await NetPerMemberAsync(householdId);
        await Assert.That(net[Ana]).IsEqualTo(-65148L);
        await Assert.That(net[Bruno]).IsEqualTo(65148L);

        await using var qs = Store.QuerySession();
        var expenses = await qs.Query<ExpenseReadModel>().Where(e => e.HouseholdId == householdId).CountAsync();
        var groups = await qs.Query<ExpenseGroupDocument>().Where(g => g.HouseholdId == householdId).CountAsync();
        await Assert.That(expenses).IsEqualTo(20);
        await Assert.That(groups).IsEqualTo(2);
    }

    [Test]
    public async Task Reversed_mapping_inverts_the_balance()
    {
        var householdId = Guid.NewGuid();

        await ImportAsync(householdId, "Casa", PlanFor(Casa), mapping: [Bruno, Ana]);

        var net = await NetPerMemberAsync(householdId);
        await Assert.That(net[Ana]).IsEqualTo(65148L);
        await Assert.That(net[Bruno]).IsEqualTo(-65148L);
    }

    [Test]
    public async Task Failed_import_leaves_nothing_behind()
    {
        var householdId = Guid.NewGuid();
        var push = Substitute.For<IPushSender>();

        await Assert.That(async () => await ImportAsync(householdId, "Casa", PlanFor(Casa), push: push,
                configure: s => s.AddTransactionalEventHandler<ExpenseRecorded, ThrowOnFifthExpense>()))
            .Throws<InvalidOperationException>();

        await using var qs = Store.QuerySession();
        await Assert.That(await qs.Query<ExpenseGroupDocument>().Where(g => g.HouseholdId == householdId).CountAsync()).IsEqualTo(0);
        await Assert.That(await qs.Query<ExpenseReadModel>().Where(e => e.HouseholdId == householdId).CountAsync()).IsEqualTo(0);
        await Assert.That(await qs.Query<ActivityEntry>().Where(a => a.HouseholdId == householdId).CountAsync()).IsEqualTo(0);
        await Assert.That(await qs.LoadAsync<HouseholdLedger>(householdId)).IsNull();
        await Assert.That(push.ReceivedCalls().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task Same_day_rows_keep_distinct_ordered_activity_times()
    {
        var householdId = Guid.NewGuid();
        const string csv = "Data,Descrição,Categoria,Custo,Moeda,Ana Lima,Bruno Costa\n" +
            "\n" +
            "2025-11-02,Primeiro,Geral,10.00,BRL,5.00,-5.00\n" +
            "2025-11-02,Segundo,Geral,20.00,BRL,10.00,-10.00\n" +
            "\n" +
            "2025-11-30,Saldo total, , ,BRL,15.00,-15.00\n";
        var plan = SplitwiseImportPlanner.Build(SplitwiseCsvParser.Parse(csv).File!).Plan!;

        await ImportAsync(householdId, "Casa", plan);

        await using var qs = Store.QuerySession();
        var activity = await qs.Query<ActivityEntry>().Where(a => a.HouseholdId == householdId).ToListAsync();
        var first = activity.Single(a => a.Description == "Primeiro");
        var second = activity.Single(a => a.Description == "Segundo");
        await Assert.That(first.OccurredAt).IsLessThan(second.OccurredAt);
        var expenses = await qs.Query<ExpenseReadModel>().Where(e => e.HouseholdId == householdId).ToListAsync();
        var midnight = new DateTimeOffset(2025, 11, 2, 0, 0, 0, TimeSpan.Zero);
        await Assert.That(expenses.All(e => e.Date == midnight)).IsTrue();
    }

    [Test]
    public async Task Import_records_every_row_with_its_original_date_payer_and_group()
    {
        var householdId = Guid.NewGuid();

        var (groupId, _) = await ImportAsync(householdId, "Casa", PlanFor(Casa));

        await using var qs = Store.QuerySession();
        var expenses = await qs.Query<ExpenseReadModel>().Where(e => e.HouseholdId == householdId).ToListAsync();
        await Assert.That(expenses.Count).IsEqualTo(15);
        await Assert.That(expenses.All(e => e.ExpenseGroupId == groupId)).IsTrue();
        await Assert.That(expenses.All(e => e.FundingSources.Sum(f => f.Cents) == e.TotalCents)).IsTrue();

        var pizza = expenses.Single(e => e.Description == "Pizza, refrigerante e sobremesa");
        await Assert.That(pizza.Date).IsEqualTo(new DateTimeOffset(2025, 11, 12, 0, 0, 0, TimeSpan.Zero));
        await Assert.That(pizza.FundingSources.Single().UserId).IsEqualTo(Bruno);

        var activity = await qs.Query<ActivityEntry>().Where(a => a.HouseholdId == householdId).ToListAsync();
        await Assert.That(activity.Count).IsEqualTo(18);
        await Assert.That(activity.Min(a => a.OccurredAt).UtcDateTime.Date).IsEqualTo(new DateTime(2025, 11, 2));
        var pizzaEntry = activity.Single(a => a.Description == "Pizza, refrigerante e sobremesa");
        await Assert.That(pizzaEntry.ActorUserId).IsEqualTo(Bruno);
        await Assert.That(pizzaEntry.GroupName).IsEqualTo("Casa");
    }

    [Test]
    public async Task Settlements_remember_their_group()
    {
        var householdId = Guid.NewGuid();

        var (groupId, _) = await ImportAsync(householdId, "Casa", PlanFor(Casa));

        await using var qs = Store.QuerySession();
        var settlements = await qs.Events.QueryRawEventDataOnly<SettlementRecorded>()
            .Where(s => s.HouseholdId == householdId).ToListAsync();
        await Assert.That(settlements.Count).IsEqualTo(3);
        await Assert.That(settlements.All(s => s.ExpenseGroupId == groupId)).IsTrue();
    }

    [Test]
    public async Task Import_sends_no_push_notifications()
    {
        var (_, push) = await ImportAsync(Guid.NewGuid(), "Casa", PlanFor(Casa));

        await Assert.That(push.ReceivedCalls().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task Second_import_with_the_same_group_name_is_refused_and_writes_nothing()
    {
        var householdId = Guid.NewGuid();
        await ImportAsync(householdId, "Casa", PlanFor(Casa));

        await Assert.That(async () => await ImportAsync(householdId, " casa ", PlanFor(Viagem)))
            .Throws<InvalidOperationException>();

        await using var qs = Store.QuerySession();
        var count = await qs.Query<ExpenseReadModel>().Where(e => e.HouseholdId == householdId).CountAsync();
        await Assert.That(count).IsEqualTo(15);
    }

    [Test]
    public async Task Unbalanced_plan_is_refused_before_anything_is_written()
    {
        var householdId = Guid.NewGuid();
        var plan = PlanFor(Casa) with { ReportedNetCents = [1, -1] };

        await Assert.That(async () => await ImportAsync(householdId, "Casa", plan))
            .Throws<InvalidOperationException>();

        await using var qs = Store.QuerySession();
        var groups = await qs.Query<ExpenseGroupDocument>().Where(g => g.HouseholdId == householdId).CountAsync();
        await Assert.That(groups).IsEqualTo(0);
    }
}

file sealed class ThrowOnFifthExpense : ITransactionalEventHandler<ExpenseRecorded>
{
    private int _seen;

    public Task HandleAsync(ExpenseRecorded @event, CancellationToken ct = default) =>
        ++_seen == 5 ? throw new InvalidOperationException("boom") : Task.CompletedTask;
}
