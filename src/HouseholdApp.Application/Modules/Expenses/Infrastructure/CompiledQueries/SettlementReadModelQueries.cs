using System.Linq.Expressions;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using Marten;
using Marten.Linq;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.CompiledQueries;

public sealed class SettlementsForHousehold : ICompiledListQuery<SettlementReadModel>
{
    public Guid HouseholdId { get; set; }

    public Expression<Func<IMartenQueryable<SettlementReadModel>, IEnumerable<SettlementReadModel>>> QueryIs() =>
        q => q.Where(s => s.HouseholdId == HouseholdId)
              .OrderByDescending(s => s.Date);
}

public sealed class SettlementsForGroup : ICompiledListQuery<SettlementReadModel>
{
    public Guid HouseholdId { get; set; }
    public Guid GroupId { get; set; }

    public Expression<Func<IMartenQueryable<SettlementReadModel>, IEnumerable<SettlementReadModel>>> QueryIs() =>
        q => q.Where(s => s.HouseholdId == HouseholdId && s.ExpenseGroupId == GroupId)
              .OrderByDescending(s => s.Date);
}
