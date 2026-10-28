using HouseholdApp.Application.Modules.Expenses.Application.Import;

namespace HouseholdApp.UnitTests.Modules.Expenses.Import;

public sealed class SplitwiseImportPlannerTests
{
    private static readonly DateOnly Day = new(2025, 11, 2);

    private static ParsedSplitwiseFile File(long[] reported, params SplitwiseRow[] rows) =>
        new(["Ana Lima", "Bruno Costa"], rows, reported);

    private static SplitwiseRow Expense(long cost, long anaNet, long? brunoNet = null) =>
        new(3, Day, "Mercado", SplitwiseRowKind.Expense, cost, [anaNet, brunoNet ?? -anaNet]);

    private static SplitwiseRow Payment(long cost, long anaNet) =>
        new(3, Day, "Pagamento", SplitwiseRowKind.Payment, cost, [anaNet, -anaNet]);

    [Test]
    public async Task Half_split_paid_by_first_person()
    {
        var result = SplitwiseImportPlanner.Build(File([5000, -5000], Expense(10000, 5000)));

        await Assert.That(result.Errors.Count).IsEqualTo(0);
        var e = result.Plan!.Expenses.Single();
        await Assert.That(e.PayerColumn).IsEqualTo(0);
        await Assert.That(e.CostCents).IsEqualTo(10000L);
        await Assert.That(e.PayerShareCents).IsEqualTo(5000L);
        await Assert.That(e.OtherShareCents).IsEqualTo(5000L);
    }

    [Test]
    public async Task Full_share_owed_by_the_other_person()
    {
        var result = SplitwiseImportPlanner.Build(File([-4590, 4590], Expense(4590, -4590)));

        var e = result.Plan!.Expenses.Single();
        await Assert.That(e.PayerColumn).IsEqualTo(1);
        await Assert.That(e.PayerShareCents).IsEqualTo(0L);
        await Assert.That(e.OtherShareCents).IsEqualTo(4590L);
    }

    [Test]
    public async Task Odd_centavo_stays_with_the_payer_and_shares_sum_to_cost()
    {
        var result = SplitwiseImportPlanner.Build(File([-9366, 9366], Expense(18733, -9366)));

        var e = result.Plan!.Expenses.Single();
        await Assert.That(e.PayerShareCents).IsEqualTo(9367L);
        await Assert.That(e.OtherShareCents).IsEqualTo(9366L);
        await Assert.That(e.PayerShareCents + e.OtherShareCents).IsEqualTo(e.CostCents);
    }

    [Test]
    public async Task Payment_becomes_settlement_from_the_positive_column()
    {
        var result = SplitwiseImportPlanner.Build(File([-30000, 30000], Payment(30000, -30000)));

        var s = result.Plan!.Settlements.Single();
        await Assert.That(s.PayerColumn).IsEqualTo(1);
        await Assert.That(s.Cents).IsEqualTo(30000L);
        await Assert.That(result.Plan.Expenses.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Payment_amount_must_equal_its_cost()
    {
        var result = SplitwiseImportPlanner.Build(File([20000, -20000], Payment(30000, 20000)));

        await Assert.That(result.Plan).IsNull();
        await Assert.That(result.Errors.Single().Code).IsEqualTo(ImportErrorCode.PaymentMismatch);
    }

    [Test]
    public async Task Share_larger_than_cost_is_rejected()
    {
        var result = SplitwiseImportPlanner.Build(File([1500, -1500], Expense(1000, 1500)));

        await Assert.That(result.Errors.Single().Code).IsEqualTo(ImportErrorCode.ShareExceedsCost);
    }

    [Test]
    public async Task Row_that_does_not_net_to_zero_is_rejected()
    {
        var result = SplitwiseImportPlanner.Build(File([100, -50], Expense(100, 100, -50)));

        await Assert.That(result.Errors.Single().Code).IsEqualTo(ImportErrorCode.RowNotBalanced);
    }

    [Test]
    public async Task All_zero_row_is_rejected()
    {
        var result = SplitwiseImportPlanner.Build(File([0, 0], Expense(100, 0)));

        await Assert.That(result.Errors.Single().Code).IsEqualTo(ImportErrorCode.ZeroRow);
    }

    [Test]
    public async Task Total_mismatch_keeps_the_plan_for_display_and_reports_an_error()
    {
        var result = SplitwiseImportPlanner.Build(File([4000, -4000], Expense(10000, 5000)));

        await Assert.That(result.Plan).IsNotNull();
        await Assert.That(result.Plan!.IsBalanced).IsFalse();
        await Assert.That(result.Plan.ComputedNetCents.SequenceEqual([5000L, -5000L])).IsTrue();
        await Assert.That(result.Errors.Single().Code).IsEqualTo(ImportErrorCode.TotalMismatch);
    }

    [Test]
    [Arguments(SplitwiseFixtures.Casa, -65148L, 15, 3)]
    [Arguments(SplitwiseFixtures.Viagem, 0L, 5, 1)]
    public async Task Fictional_exports_reproduce_the_Splitwise_total(
        string fixture, long anaNet, int expenses, int settlements)
    {
        var parsed = SplitwiseCsvParser.Parse(SplitwiseFixtures.Read(fixture));

        var result = SplitwiseImportPlanner.Build(parsed.File!);

        await Assert.That(result.Errors.Count).IsEqualTo(0);
        var plan = result.Plan!;
        await Assert.That(plan.IsBalanced).IsTrue();
        await Assert.That(plan.ComputedNetCents.SequenceEqual([anaNet, -anaNet])).IsTrue();
        await Assert.That(plan.Expenses.Count).IsEqualTo(expenses);
        await Assert.That(plan.Settlements.Count).IsEqualTo(settlements);
        await Assert.That(plan.Expenses.All(e => e.PayerShareCents + e.OtherShareCents == e.CostCents)).IsTrue();
    }

    [Test]
    public async Task Computed_nets_come_from_the_planned_records()
    {
        var result = SplitwiseImportPlanner.Build(File([2500, -2500], Expense(10000, 3000), Payment(500, -500)));

        await Assert.That(result.Errors.Count).IsEqualTo(0);
        var plan = result.Plan!;
        await Assert.That(plan.IsBalanced).IsTrue();
        var expected = new long[2];
        foreach (var e in plan.Expenses)
        {
            expected[e.PayerColumn] += e.CostCents - e.PayerShareCents;
            expected[1 - e.PayerColumn] -= e.OtherShareCents;
        }
        foreach (var s in plan.Settlements)
        {
            expected[s.PayerColumn] += s.Cents;
            expected[1 - s.PayerColumn] -= s.Cents;
        }
        await Assert.That(plan.ComputedNetCents.SequenceEqual(expected)).IsTrue();
        await Assert.That(plan.Expenses.Single().PayerShareCents).IsEqualTo(7000L);
        await Assert.That(plan.Expenses.Single().OtherShareCents).IsEqualTo(3000L);
    }

    [Test]
    public async Task Mapping_requires_both_columns()
    {
        var errors = SplitwiseImportPlanner.ValidateMapping([Guid.NewGuid(), null], new HashSet<Guid>());

        await Assert.That(errors.Single().Code).IsEqualTo(ImportErrorCode.Unmapped);
    }

    [Test]
    public async Task Mapping_requires_two_different_members()
    {
        var member = Guid.NewGuid();

        var errors = SplitwiseImportPlanner.ValidateMapping([member, member], new HashSet<Guid> { member });

        await Assert.That(errors.Single().Code).IsEqualTo(ImportErrorCode.DuplicateMapping);
    }

    [Test]
    public async Task Mapping_requires_household_members()
    {
        var member = Guid.NewGuid();

        var errors = SplitwiseImportPlanner.ValidateMapping([member, Guid.NewGuid()], new HashSet<Guid> { member });

        await Assert.That(errors.Single().Code).IsEqualTo(ImportErrorCode.NotAMember);
    }

    [Test]
    public async Task Valid_mapping_has_no_errors()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();

        var errors = SplitwiseImportPlanner.ValidateMapping([a, b], new HashSet<Guid> { a, b });

        await Assert.That(errors.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Local confidence check against real Splitwise exports kept outside the repo:
    /// SPLITWISE_EXPORTS_DIR=&lt;dir&gt; dotnet test tests/HouseholdApp.UnitTests
    ///   --treenode-filter "/*/*/SplitwiseImportPlannerTests/Real_exports_reproduce_the_Splitwise_total"
    /// </summary>
    [Test]
    [Explicit]
    public async Task Real_exports_reproduce_the_Splitwise_total()
    {
        var dir = Environment.GetEnvironmentVariable("SPLITWISE_EXPORTS_DIR");
        await Assert.That(dir).IsNotNull();

        var files = Directory.GetFiles(dir!, "*_export*.csv");
        await Assert.That(files.Length).IsGreaterThan(0);

        foreach (var path in files)
        {
            var parsed = SplitwiseCsvParser.Parse(await System.IO.File.ReadAllTextAsync(path));
            await Assert.That(parsed.Errors.Count).IsEqualTo(0).Because(Path.GetFileName(path));

            var result = SplitwiseImportPlanner.Build(parsed.File!);
            await Assert.That(result.Errors.Count).IsEqualTo(0).Because(Path.GetFileName(path));
            await Assert.That(result.Plan!.IsBalanced).IsTrue().Because(Path.GetFileName(path));
        }
    }
}
