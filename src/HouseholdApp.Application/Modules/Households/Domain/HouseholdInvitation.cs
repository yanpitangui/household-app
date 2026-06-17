namespace HouseholdApp.Application.Modules.Households.Domain;

public enum InvitationStatus { Pending, Accepted, Revoked }

public sealed class HouseholdInvitation
{
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string Token { get; private set; } = default!;
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public InvitationStatus Status { get; private set; }
    public Guid? AcceptedBy { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }

    private HouseholdInvitation() { }

    internal static HouseholdInvitation Create(Guid householdId, Guid createdBy, DateTimeOffset now, TimeSpan expiry)
    {
        return new HouseholdInvitation
        {
            Id = Guid.CreateVersion7(),
            HouseholdId = householdId,
            Token = GenerateToken(),
            CreatedBy = createdBy,
            CreatedAt = now,
            ExpiresAt = now.Add(expiry),
            Status = InvitationStatus.Pending
        };
    }

    internal void Consume(Guid userId, DateTimeOffset now)
    {
        if (Status != InvitationStatus.Pending)
            throw new InvalidOperationException("Invitation is no longer valid.");
        if (now > ExpiresAt)
            throw new InvalidOperationException("Invitation has expired.");

        Status = InvitationStatus.Accepted;
        AcceptedBy = userId;
        AcceptedAt = now;
    }

    public void Revoke()
    {
        if (Status != InvitationStatus.Pending)
            throw new InvalidOperationException("Only pending invitations can be revoked.");
        Status = InvitationStatus.Revoked;
    }

    private static string GenerateToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
