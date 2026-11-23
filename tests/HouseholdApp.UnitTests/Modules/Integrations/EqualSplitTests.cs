using HouseholdApp.Application.Modules.Integrations.Application.Operations;

namespace HouseholdApp.UnitTests.Modules.Integrations;

public sealed class EqualSplitTests
{
    [Test]
    public async Task Splits_equally_with_the_remainder_to_the_payer()
    {
        Guid ana = Guid.NewGuid(), leo = Guid.NewGuid();

        var allocations = EqualSplit.Allocate(1001, [ana, leo], payerId: leo);

        await Assert.That(allocations.Single(a => a.UserId == ana).Cents).IsEqualTo(500L);
        await Assert.That(allocations.Single(a => a.UserId == leo).Cents).IsEqualTo(501L);
    }

    [Test]
    public async Task A_single_member_takes_everything()
    {
        var ana = Guid.NewGuid();

        var allocations = EqualSplit.Allocate(999, [ana], payerId: ana);

        await Assert.That(allocations.Single().Cents).IsEqualTo(999L);
    }

    [Test]
    public async Task No_members_is_rejected()
    {
        await Assert.That(() => EqualSplit.Allocate(100, [], Guid.NewGuid())).Throws<ArgumentException>();
    }
}
