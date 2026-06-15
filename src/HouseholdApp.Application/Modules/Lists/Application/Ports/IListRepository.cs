using HouseholdApp.Application.Modules.Lists.Domain;

namespace HouseholdApp.Application.Modules.Lists.Application.Ports;

public interface IListRepository
{
    Task SaveListAsync(HouseholdList list, CancellationToken ct = default);
    Task<HouseholdList?> GetAsync(Guid listId, CancellationToken ct = default);
    Task DeleteItemAsync(Guid itemId, CancellationToken ct = default);
    Task DeleteListAsync(Guid listId, CancellationToken ct = default);
}
