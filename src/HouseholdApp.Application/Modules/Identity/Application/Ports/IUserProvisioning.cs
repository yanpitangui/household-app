namespace HouseholdApp.Application.Modules.Identity.Application.Ports;

public interface IUserProvisioning
{
    Task ProvisionAsync(string subject, string email, string displayName, string? pictureUrl, CancellationToken ct = default);
}
