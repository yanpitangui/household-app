namespace HouseholdApp.Application.Modules.Identity.Application.Ports;

public interface IUserLocales
{
    // Stores the culture name (e.g. "pt-BR") the user reads the app in. A no-op when it is unchanged.
    Task SetLocaleAsync(Guid userId, string locale, CancellationToken ct = default);
}
