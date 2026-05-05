namespace NServiceBus.PersistenceTesting;

using Messaging.Persistence.Sqlite;
using Messaging.Persistence.Sqlite.Outbox;
using Messaging.Persistence.Sqlite.Sagas;
using Microsoft.Data.Sqlite;
using NServiceBus.Outbox;
using NServiceBus.Sagas;
using Persistence;

public partial class PersistenceTestsConfiguration
{
    public bool SupportsDtc => false;

    public bool SupportsOutbox => true;

    public bool SupportsFinders => false;

    public bool SupportsPessimisticConcurrency => false;

    public ISagaIdGenerator SagaIdGenerator { get; } = new DefaultSagaIdGenerator();

    public Func<ICompletableSynchronizedStorageSession> CreateStorageSession { get; private set; } = null!;

    public ISagaPersister SagaStorage { get; private set; } = null!;

    public IOutboxStorage OutboxStorage { get; private set; } = null!;

    string dbPath = null!;
    DefaultConnectionFactory factory = null!;

    public async Task Configure(CancellationToken cancellationToken = default)
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"messaging-sqlite-persistencetests-{Guid.NewGuid():N}.db");
        factory = new DefaultConnectionFactory($"Data Source={dbPath}");

        var sagaInfoCache = new SagaInfoCache(tablePrefix: "");

        await using (var connection = await factory.OpenConnection(cancellationToken).ConfigureAwait(false))
        {
            await ExecuteScript(connection, SchemaScripts.CreateOutboxTable(tablePrefix: ""), cancellationToken).ConfigureAwait(false);

            foreach (var sagaMetadata in SagaMetadataCollection)
            {
                var info = sagaInfoCache.Get(sagaMetadata.SagaEntityType);
                await ExecuteScript(connection, SchemaScripts.CreateSagaTable(info.TableName), cancellationToken).ConfigureAwait(false);
            }
        }

        SagaStorage = new SqliteSagaPersister(sagaInfoCache);
        OutboxStorage = new SqliteOutboxPersister(factory, tablePrefix: "", endpointName: "PersistenceTests");
        CreateStorageSession = () => new SqliteSynchronizedStorageSession(factory);
    }

    public Task Cleanup(CancellationToken cancellationToken = default)
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(dbPath); }
        catch (IOException) { /* file may briefly remain locked */ }
        return Task.CompletedTask;
    }

    static async Task ExecuteScript(SqliteConnection connection, string script, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = script;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
