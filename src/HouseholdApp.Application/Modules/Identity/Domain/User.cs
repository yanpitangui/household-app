namespace HouseholdApp.Application.Modules.Identity.Domain;

public sealed class User
{
    public Guid Id { get; private set; }
    public string Subject { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastLoginAt { get; private set; }

    private User() { }

    public static User Provision(string subject, string email, string displayName, DateTimeOffset now)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Subject = subject,
            Email = email,
            DisplayName = displayName,
            CreatedAt = now,
            LastLoginAt = now
        };
    }

}
