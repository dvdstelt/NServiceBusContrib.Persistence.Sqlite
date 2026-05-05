namespace Messaging.Persistence.Sqlite.Sagas;

using NServiceBus.Installation;
using NServiceBus.Sagas;

sealed class SagaInstaller(
    IConnectionFactory connectionFactory,
    SagaInfoCache sagaInfoCache,
    SagaMetadataCollection sagaMetadata) : INeedToInstallSomething
{
    public async Task Install(string identity, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnection(cancellationToken).ConfigureAwait(false);

        foreach (var metadata in sagaMetadata)
        {
            var info = sagaInfoCache.Get(metadata.SagaEntityType);
            await using var command = connection.CreateCommand();
            command.CommandText = SchemaScripts.CreateSagaTable(info.TableName);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
