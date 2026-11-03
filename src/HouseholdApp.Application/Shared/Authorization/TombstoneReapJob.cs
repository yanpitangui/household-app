using HouseholdApp.Application.Shared.Scheduler;
using Microsoft.Extensions.Logging;
using Valtuutus.Data;

namespace HouseholdApp.Application.Shared.Authorization;

/// <summary>
/// Hourly: clears the authorization tuples and attributes that removed memberships and role changes tombstoned, once they
/// are older than any snap token a reader can still hold (Valtuutus's retention, 7 days by default).
/// </summary>
public sealed partial class TombstoneReapJob(ITombstoneReaper reaper, ILogger<TombstoneReapJob> logger) : IScheduledJob
{
    public const string Name = "AuthzTombstoneReap";
    public const string Cron = "17 * * * *";

    public async Task RunAsync(CancellationToken ct)
    {
        var result = await reaper.ReapAsync(ct);
        LogReaped(result.RelationTuplesDeleted, result.AttributesDeleted, result.TransactionsDeleted, result.BatchesRun);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Reaped authorization tombstones: {RelationTuples} relation tuples, {Attributes} attributes, {Transactions} transactions in {Batches} batches")]
    private partial void LogReaped(int relationTuples, int attributes, int transactions, int batches);
}
