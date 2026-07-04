using System.Linq.Expressions;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using Marten;
using Marten.Linq;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.CompiledQueries;

public sealed class ExpenseGroupsForHousehold : ICompiledListQuery<ExpenseGroupDocument>
{
    public Guid HouseholdId { get; set; }

    public Expression<Func<IMartenQueryable<ExpenseGroupDocument>, IEnumerable<ExpenseGroupDocument>>> QueryIs() =>
        q => q.Where(g => g.HouseholdId == HouseholdId).OrderBy(g => g.Name);
}
