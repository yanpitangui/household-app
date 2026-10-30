using Quartz;

namespace HouseholdApp.Application.Shared.Scheduler;

internal static class QuartzCron
{
    /// <summary>
    /// Reads the 5-field cron the app stores (Unix dialect: no seconds, Sunday is 0 or 7) into
    /// Quartz's canonical form, which is what a stored trigger reports back.
    /// </summary>
    public static string FromUnix(string cron) =>
        CronExpression.TryParse(cron, CronFormat.Unix, out var expression)
            ? expression.CronExpressionString!
            : throw new FormatException($"'{cron}' is not a 5-field cron expression.");
}
