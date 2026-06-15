namespace HouseholdApp.Application.Modules.Identity.Application.Ports;

public interface IUserProvisioning
{
    Task ProvisionAsync(string subject, string email, string displayName, CancellationToken ct = default);
}
