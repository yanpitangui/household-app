using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Persistence;
using Valtuutus.Core;
using Valtuutus.Core.Data;
using Valtuutus.Data.Db;
using Valtuutus.Lang;

namespace HouseholdApp.Application.Modules.Households.Infrastructure;

public sealed class HouseholdAuthorizationSyncHandler(IDbDataWriterProvider writer, IUnitOfWork uow)
    : ITransactionalEventHandler<HouseholdCreated>,
      ITransactionalEventHandler<HouseholdMemberJoined>,
      ITransactionalEventHandler<HouseholdMemberRemoved>,
      ITransactionalEventHandler<HouseholdRoleChanged>
{
    public async Task HandleAsync(HouseholdCreated evt, CancellationToken ct)
    {
        var conn = await uow.GetConnectionAsync(ct);
        await writer.Write(conn, uow.CurrentTransaction!,
            [new RelationTuple(
                SchemaConstsGen.Household.Name, evt.HouseholdId.ToString(),
                SchemaConstsGen.Household.Relations.Owner,
                SchemaConstsGen.User.Name, evt.OwnerId.ToString())],
            [], ct);
    }

    public async Task HandleAsync(HouseholdMemberJoined evt, CancellationToken ct)
    {
        var conn = await uow.GetConnectionAsync(ct);
        await writer.Write(conn, uow.CurrentTransaction!,
            [new RelationTuple(
                SchemaConstsGen.Household.Name, evt.HouseholdId.ToString(),
                ToRelation(evt.Role),
                SchemaConstsGen.User.Name, evt.UserId.ToString())],
            [], ct);
    }

    public async Task HandleAsync(HouseholdMemberRemoved evt, CancellationToken ct) =>
        await DeleteAllRelations(evt.HouseholdId, evt.UserId, ct);

    public async Task HandleAsync(HouseholdRoleChanged evt, CancellationToken ct)
    {
        await DeleteAllRelations(evt.HouseholdId, evt.UserId, ct);
        var conn = await uow.GetConnectionAsync(ct);
        await writer.Write(conn, uow.CurrentTransaction!,
            [new RelationTuple(
                SchemaConstsGen.Household.Name, evt.HouseholdId.ToString(),
                ToRelation(evt.NewRole),
                SchemaConstsGen.User.Name, evt.UserId.ToString())],
            [], ct);
    }

    private async Task DeleteAllRelations(Guid householdId, Guid userId, CancellationToken ct)
    {
        var conn = await uow.GetConnectionAsync(ct);
        await writer.Delete(conn, uow.CurrentTransaction!, new DeleteFilter
        {
            Relations =
            [
                new DeleteRelationsFilter
                {
                    EntityType = SchemaConstsGen.Household.Name,
                    EntityId = householdId.ToString(),
                    SubjectType = SchemaConstsGen.User.Name,
                    SubjectId = userId.ToString()
                }
            ]
        }, ct);
    }

    private static string ToRelation(HouseholdRole role) => role switch
    {
        HouseholdRole.Owner => SchemaConstsGen.Household.Relations.Owner,
        HouseholdRole.Admin => SchemaConstsGen.Household.Relations.Admin,
        HouseholdRole.Member => SchemaConstsGen.Household.Relations.Member,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };
}
