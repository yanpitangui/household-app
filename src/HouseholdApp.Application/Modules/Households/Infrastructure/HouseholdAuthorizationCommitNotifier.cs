using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Shared.Events;
using Valtuutus.Core.Data;
using Valtuutus.Data.Db;

namespace HouseholdApp.Application.Modules.Households.Infrastructure;

/// <summary>
/// Announces the authorization writes <see cref="HouseholdAuthorizationSyncHandler"/> made inside the
/// unit of work's transaction, once that transaction commits. Writes through a caller's transaction
/// don't announce themselves, and Valtuutus's cache only learns the latest snap token from these
/// announcements. A rolled-back transaction never reaches the deferred handlers, so its writes are
/// never announced.
/// </summary>
public sealed class HouseholdAuthorizationCommitNotifier(IDbDataWriterProvider writer)
    : IEventHandler<HouseholdCreated>,
      IEventHandler<HouseholdMemberJoined>,
      IEventHandler<HouseholdMemberRemoved>,
      IEventHandler<HouseholdRoleChanged>
{
    private readonly List<SnapToken> _written = [];

    public void Written(SnapToken snapToken) => _written.Add(snapToken);

    public Task HandleAsync(HouseholdCreated evt, CancellationToken ct) => AnnounceAsync();

    public Task HandleAsync(HouseholdMemberJoined evt, CancellationToken ct) => AnnounceAsync();

    public Task HandleAsync(HouseholdMemberRemoved evt, CancellationToken ct) => AnnounceAsync();

    public Task HandleAsync(HouseholdRoleChanged evt, CancellationToken ct) => AnnounceAsync();

    // Every event of the commit dispatches here; the first announces all of its writes.
    private async Task AnnounceAsync()
    {
        foreach (var snapToken in _written)
            await writer.NotifyCommitted(snapToken);
        _written.Clear();
    }
}
