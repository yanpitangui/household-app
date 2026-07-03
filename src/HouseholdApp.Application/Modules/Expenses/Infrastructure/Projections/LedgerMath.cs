using HouseholdApp.Application.Modules.Expenses.Domain;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

public static class LedgerMath
{
    public static Dictionary<Guid, long> NetPerUser(
        IReadOnlyList<FundingSource> fundingSources, IReadOnlyList<Allocation> allocations)
    {
        var net = new Dictionary<Guid, long>();
        foreach (var f in fundingSources) net[f.UserId] = net.GetValueOrDefault(f.UserId) + f.Cents;
        foreach (var a in allocations) net[a.UserId] = net.GetValueOrDefault(a.UserId) - a.Cents;
        return net;
    }
}
