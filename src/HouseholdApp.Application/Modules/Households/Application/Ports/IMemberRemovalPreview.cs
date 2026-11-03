using HouseholdApp.Application.Modules.Expenses.Application.Ports;

namespace HouseholdApp.Application.Modules.Households.Application.Ports;

public sealed record MemberRemovalPreview(
    HouseholdMemberDto Member,
    ExpenseRemovalImpact Expenses,
    int OpenTasks);

public interface IMemberRemovalPreview
{
    Task<MemberRemovalPreview?> GetAsync(Guid householdId, Guid userId, CancellationToken ct = default);
}
