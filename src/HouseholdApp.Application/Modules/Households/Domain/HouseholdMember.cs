namespace HouseholdApp.Application.Modules.Households.Domain;

public sealed class HouseholdMember(Guid householdId, Guid userId, HouseholdRole role)
{
    public Guid HouseholdId { get; } = householdId;
    public Guid UserId { get; } = userId;
    public HouseholdRole Role { get; private set; } = role;

    internal void SetRole(HouseholdRole role) => Role = role;
}
