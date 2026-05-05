namespace Messaging.Persistence.Sqlite.Outbox;

using Microsoft.Data.Sqlite;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Logging;

sealed class OutboxCleaner(
    IConnectionFactory connectionFactory,
    string tablePrefix,
    string endpointName,
    TimeSpan retentionPeriod,
    TimeSpan cleanupFrequency) : FeatureStartupTask
{
    public const int CleanupBatchSize = 4000;

    static readonly ILog Log = LogManager.GetLogger<OutboxCleaner>();

    Task? cleanupLoop;
    CancellationTokenSource? cts;

    protected override Task OnStart(IMessageSession session, CancellationToken cancellationToken = default)
    {
        cts = new CancellationTokenSource();
        cleanupLoop = Task.Run(() => RunCleanupLoop(cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    protected override async Task OnStop(IMessageSession session, CancellationToken cancellationToken = default)
    {
        if (cts is null)
        {
            return;
        }

        await cts.CancelAsync().ConfigureAwait(false);
        if (cleanupLoop is not null)
        {
            // RunCleanupLoop swallows OperationCanceledException internally, so the awaited
            // task should always RanToCompletion on a clean shutdown. Anything else is a real
            // failure and should surface here.
            await cleanupLoop.ConfigureAwait(false);
        }
        cts.Dispose();
        cts = null;
    }

    async Task RunCleanupLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(cleanupFrequency, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await TryCleanupOnceAsync(connectionFactory, tablePrefix, endpointName, retentionPeriod, CleanupBatchSize, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs one cleanup pass. Expected database/IO failures are logged and swallowed so the
    /// loop continues; truly unexpected exceptions propagate so the loop crashes loudly rather
    /// than silently spinning on a permanently broken state.
    /// </summary>
    internal static async Task TryCleanupOnceAsync(
        IConnectionFactory connectionFactory,
        string tablePrefix,
        string endpointName,
        TimeSpan retentionPeriod,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await CleanupOnce(connectionFactory, tablePrefix, endpointName, retentionPeriod, batchSize, cancellationToken)
                .ConfigureAwait(false);
            if (deleted > 0)
            {
                Log.InfoFormat("Outbox cleanup removed {0} dispatched record(s).", deleted);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SqliteException ex)
        {
            Log.Error("Outbox cleanup failed with a SQLite error.", ex);
        }
        catch (IOException ex)
        {
            Log.Error("Outbox cleanup failed with an I/O error.", ex);
        }
    }

    internal static async Task<int> CleanupOnce(
        IConnectionFactory connectionFactory,
        string tablePrefix,
        string endpointName,
        TimeSpan retentionPeriod,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var cutoff = (DateTime.UtcNow - retentionPeriod).ToString("O");
        await using var connection = await connectionFactory.OpenConnection(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM {tablePrefix}OutboxRecord
            WHERE EndpointName = $ep
              AND MessageId IN (
                  SELECT MessageId FROM {tablePrefix}OutboxRecord
                  WHERE EndpointName = $ep AND Dispatched = 1 AND DispatchedAt < $cutoff
                  LIMIT $limit
              );
            """;
        command.Parameters.AddWithValue("$ep", endpointName);
        command.Parameters.AddWithValue("$cutoff", cutoff);
        command.Parameters.AddWithValue("$limit", batchSize);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
