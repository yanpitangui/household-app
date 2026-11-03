using HouseholdApp.Application.Modules.Expenses.Domain;

namespace HouseholdApp.UnitTests.Modules.Expenses.Domain;

public sealed class MemberRemovalTests
{
    private static readonly Guid Ana = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bia = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Leo = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    [Test]
    public async Task Untouched_when_the_member_has_no_part()
    {
        var outcome = MemberRemoval.For([new(Ana, 100_00)], [new(Ana, 50_00), new(Leo, 50_00)], Bia);
        await Assert.That(outcome).IsTypeOf<MemberRemoval.Unaffected>();
    }

    [Test]
    public async Task Share_goes_to_the_others_in_proportion_keeping_every_cent()
    {
        // 100.00 split 33.34 / 33.33 / 33.33; Bia leaves with 33.33 to hand over, split 3334:3333 between Ana and Leo.
        // Ana: 3333 * 3334 / 6667 = 1666.75, Leo: 3333 * 3333 / 6667 = 1666.25. Whole cents give 1666 each, so the 1
        // cent left over goes to the larger remainder (.75) and Ana takes 1667 (total 50.01) and Leo 1666 (total 49.99); together exactly 100.00.
        var outcome = MemberRemoval.For([new(Ana, 100_00)],
            [new(Ana, 33_34), new(Bia, 33_33), new(Leo, 33_33)], Bia);
        var shares = ((MemberRemoval.Redistributed)outcome).Allocations;
        await Assert.That(shares.Select(a => a.UserId)).IsEquivalentTo(new[] { Ana, Leo });
        await Assert.That(shares.Sum(a => a.Cents)).IsEqualTo(100_00);
        await Assert.That(shares.Single(a => a.UserId == Ana).Cents).IsEqualTo(50_01);
        await Assert.That(shares.Single(a => a.UserId == Leo).Cents).IsEqualTo(49_99);
    }

    [Test]
    public async Task Zero_shares_split_equally()
    {
        var outcome = MemberRemoval.For([new(Ana, 90_00)], [new(Ana, 0), new(Bia, 90_00), new(Leo, 0)], Bia);
        var shares = ((MemberRemoval.Redistributed)outcome).Allocations;
        await Assert.That(shares.Select(a => a.Cents)).IsEquivalentTo(new[] { 45_00L, 45_00L });
    }

    [Test]
    public async Task Ends_when_the_member_pays()
    {
        var outcome = MemberRemoval.For([new(Bia, 100_00)], [new(Ana, 50_00), new(Bia, 50_00)], Bia);
        await Assert.That(outcome).IsTypeOf<MemberRemoval.Ended>();
    }

    [Test]
    public async Task Ends_when_only_the_payer_would_be_left()
    {
        var outcome = MemberRemoval.For([new(Ana, 100_00)], [new(Ana, 50_00), new(Bia, 50_00)], Bia);
        await Assert.That(outcome).IsTypeOf<MemberRemoval.Ended>();
    }
}
