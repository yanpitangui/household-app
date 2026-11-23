using HouseholdApp.Application.Modules.Expenses.Application.Ports;

namespace HouseholdApp.Application.Modules.Integrations.Application.Operations;

// Same default as the Record page: equal shares; the leftover cents go to the payer.
public static class EqualSplit
{
    public static IReadOnlyList<AllocationDto> Allocate(long cents, IReadOnlyList<Guid> memberIds, Guid payerId)
    {
        // Callers pass the household's current members, so an empty list is a programming error.
        if (memberIds.Count == 0) throw new ArgumentException("At least one member is required.", nameof(memberIds));
        var share = cents / memberIds.Count;
        var remainder = cents % memberIds.Count;
        var remainderTo = memberIds.Contains(payerId) ? payerId : memberIds[0];
        return memberIds.Select(id => new AllocationDto(id, share + (id == remainderTo ? remainder : 0))).ToList();
    }
}
