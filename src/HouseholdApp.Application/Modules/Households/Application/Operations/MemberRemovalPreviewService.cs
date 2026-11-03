using HouseholdApp.Application.Modules.Expenses.Application.Ports;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Modules.Tasks.Application.Ports;

namespace HouseholdApp.Application.Modules.Households.Application.Operations;

public sealed class MemberRemovalPreviewService(
    IHouseholdQueries households, IExpenseMemberRemoval expenses, ITaskQueries tasks) : IMemberRemovalPreview
{
    public async Task<MemberRemovalPreview?> GetAsync(Guid householdId, Guid userId, CancellationToken ct = default)
    {
        var member = (await households.GetMembersAsync(householdId, ct)).FirstOrDefault(m => m.UserId == userId);
        if (member is null) return null;
        return new MemberRemovalPreview(member,
            await expenses.PreviewAsync(householdId, userId, ct),
            await tasks.CountOpenAssignedAsync(householdId, userId, ct));
    }
}
