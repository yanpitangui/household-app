using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

namespace HouseholdApp.UnitTests.Modules.Expenses.Infrastructure;

public sealed class LedgerMathTests
{
    [Test]
    public async Task NetPerUser_returns_positive_for_payer_negative_for_debtor()
    {
        var payer = Guid.NewGuid();
        var debtor = Guid.NewGuid();
        var funding = new[] { new FundingSource(payer, 1000) };
        var allocations = new[] { new Allocation(payer, 500), new Allocation(debtor, 500) };

        var net = LedgerMath.NetPerUser(funding, allocations);

        await Assert.That(net[payer]).IsEqualTo(500L);
        await Assert.That(net[debtor]).IsEqualTo(-500L);
    }

    [Test]
    public async Task NetPerUser_returns_zero_net_for_someone_who_pays_their_own_share()
    {
        var user = Guid.NewGuid();
        var funding = new[] { new FundingSource(user, 500) };
        var allocations = new[] { new Allocation(user, 500) };

        var net = LedgerMath.NetPerUser(funding, allocations);

        await Assert.That(net[user]).IsEqualTo(0L);
    }
}
