using System.Data.Common;
using KUKULCAN.SharedKernel.Database.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KUKULCAN.SharedKernel.Database.Interceptors;

/// <summary>
/// EF Core <see cref="DbCommandInterceptor"/> that logs a warning whenever a
/// database command exceeds the configured slow-query threshold.
/// </summary>
/// <remarks>
/// <para>
/// The threshold defaults to 500 ms but can be overridden via
/// <see cref="SlowQueryThresholdMs"/>. Set to <c>0</c> to log every command
/// (useful during performance profiling sessions).
/// </para>
/// <para>
/// When <see cref="KukulcanDatabaseOptions.EnableSensitiveDataLogging"/> is <c>true</c>,
/// the SQL text and parameter values are included in the log entry. Always
/// <c>false</c> in production.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Adjust threshold globally:
/// SlowQueryInterceptor.SlowQueryThresholdMs = 1000;
///
/// // Log output (WARNING level):
/// // [SlowQuery] 823ms exceeded threshold (500ms). SQL: SELECT ...
/// </code>
/// </example>
/// <param name="logger">Logger used to report slow database commands.</param>
/// <param name="options">Database options controlling sensitive-data logging.</param>
public sealed class SlowQueryInterceptor(ILogger<SlowQueryInterceptor> logger, IOptions<KukulcanDatabaseOptions> options) : DbCommandInterceptor
{
    /// <summary>
    /// Commands taking longer than this value (milliseconds) are logged as warnings.
    /// Default: 500 ms.
    /// </summary>
    public static int SlowQueryThresholdMs { get; set; } = 500;

    private readonly KukulcanDatabaseOptions _options = options.Value;

    /// <inheritdoc/>
    public override DbDataReader ReaderExecuted(DbCommand command,
        CommandExecutedEventData eventData, DbDataReader result)
    {
        LogIfSlow(command, eventData.Duration);
        return base.ReaderExecuted(command, eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
        CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData.Duration);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc/>
    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        LogIfSlow(command, eventData.Duration);
        return base.NonQueryExecuted(command, eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData.Duration);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

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
