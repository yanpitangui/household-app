using System.Linq.Expressions;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using Marten;
using Marten.Linq;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.CompiledQueries;

public sealed class LatestExpenseInGroup : ICompiledQuery<ExpenseReadModel, ExpenseReadModel?>
{
    public Guid HouseholdId { get; set; }
    public Guid GroupId { get; set; }

    public Expression<Func<IMartenQueryable<ExpenseReadModel>, ExpenseReadModel?>> QueryIs() =>
        q => q.Where(e => e.HouseholdId == HouseholdId && e.ExpenseGroupId == GroupId && !e.IsVoided)
              .OrderByDescending(e => e.Date)
              .FirstOrDefault();
}
