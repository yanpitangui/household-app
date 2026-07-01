using HouseholdApp.Application.Modules.Expenses.Domain;

namespace HouseholdApp.UnitTests.Modules.Expenses.Domain;

public sealed class ExpenseGroupTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Test]
    public async Task Create_with_no_rules_succeeds()
    {
        var group = ExpenseGroup.Create(HouseholdId, "Shared", null, [], Now);

        await Assert.That(group.HouseholdId).IsEqualTo(HouseholdId);
        await Assert.That(group.Name).IsEqualTo("Shared");
        await Assert.That(group.DefaultRules).IsEmpty();
    }

    [Test]
    public async Task Create_with_valid_rules_summing_to_100_succeeds()
    {
        var rules = new[]
        {
            new DefaultAllocationRule(Guid.NewGuid(), 60m),
            new DefaultAllocationRule(Guid.NewGuid(), 40m)
        };

        var group = ExpenseGroup.Create(HouseholdId, "Rent", null, rules, Now);

        await Assert.That(group.DefaultRules.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Create_throws_when_rules_do_not_sum_to_100()
    {
        var rules = new[]
        {
            new DefaultAllocationRule(Guid.NewGuid(), 60m),
            new DefaultAllocationRule(Guid.NewGuid(), 30m)
        };

        await Assert.That(() =>
            ExpenseGroup.Create(HouseholdId, "Bad", null, rules, Now))
            .Throws<InvalidOperationException>();
    }
}
