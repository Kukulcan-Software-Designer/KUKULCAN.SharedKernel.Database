using System.Data.Common;
using KUKULCAN.SharedKernel.Database.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KUKULCAN.SharedKernel.Database.Interceptors;

/// <summary>
/// EF Core <see cref="DbCommandInterceptor"/> that logs a warning whenever a
/// database command exceeds the configured slow-query threshold.
/// </summary>
public sealed class SlowQueryInterceptor(ILogger<SlowQueryInterceptor> logger, IOptions<KukulcanDatabaseOptions> options) : DbCommandInterceptor
{
    /// <summary>Commands taking longer than this value (milliseconds) are logged as warnings. Default: 500 ms.</summary>
    public static int SlowQueryThresholdMs { get; set; } = 500;

    private readonly KukulcanDatabaseOptions _options = options.Value;

    /// <inheritdoc/>
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        LogIfSlow(command, eventData.Duration);
        return base.ReaderExecuted(command, eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData.Duration);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc/>
    public override int ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        LogIfSlow(command, eventData.Duration);
        return base.ScalarExecuted(command, eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData.Duration);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc/>
    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        LogIfSlow(command, eventData.Duration);
        return base.NonQueryExecuted(command, eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData.Duration);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    private void LogIfSlow(DbCommand command, TimeSpan duration)
    {
        if (duration.TotalMilliseconds <= SlowQueryThresholdMs) return;

        string sql = _options.EnableSensitiveDataLogging
            ? command.CommandText
            : "[SQL hidden — EnableSensitiveDataLogging is false]";

        logger.LogWarning(
            "[SlowQuery] {ElapsedMs}ms exceeded threshold ({ThresholdMs}ms). SQL: {Sql}",
            (int)duration.TotalMilliseconds,
            SlowQueryThresholdMs,
            sql);
    }
}
