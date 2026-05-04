namespace Messaging.Persistence.Sqlite.Outbox;

using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Logging;

sealed class OutboxCleaner(
    IConnectionFactory connectionFactory,
    string tablePrefix,
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
        try
        {
            if (cleanupLoop is not null)
            {
                await cleanupLoop.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected during shutdown
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
                var deleted = await CleanupOnce(connectionFactory, tablePrefix, retentionPeriod, CleanupBatchSize, cancellationToken).ConfigureAwait(false);
                if (deleted > 0)
                {
                    Log.InfoFormat("Outbox cleanup removed {0} dispatched record(s).", deleted);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warn("Outbox cleanup iteration failed.", ex);
            }
        }
    }

    internal static async Task<int> CleanupOnce(
        IConnectionFactory connectionFactory,
        string tablePrefix,
        TimeSpan retentionPeriod,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var cutoff = (DateTime.UtcNow - retentionPeriod).ToString("O");
        await using var connection = await connectionFactory.OpenConnection(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM {tablePrefix}OutboxRecord
            WHERE MessageId IN (
                SELECT MessageId FROM {tablePrefix}OutboxRecord
                WHERE Dispatched = 1 AND DispatchedAt < $cutoff
                LIMIT $limit
            );
            """;
        command.Parameters.AddWithValue("$cutoff", cutoff);
        command.Parameters.AddWithValue("$limit", batchSize);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
