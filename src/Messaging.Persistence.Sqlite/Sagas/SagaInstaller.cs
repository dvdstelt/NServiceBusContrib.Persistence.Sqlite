namespace Messaging.Persistence.Sqlite.Sagas;

using Microsoft.Extensions.DependencyInjection;
using NServiceBus.Installation;
using NServiceBus.Sagas;

sealed class SagaInstaller(IServiceProvider serviceProvider) : INeedToInstallSomething
{
    public async Task Install(string identity, CancellationToken cancellationToken = default)
    {
        // INeedToInstallSomething is discovered by assembly scanning, so this installer can be
        // activated on endpoints that did not enable sagas. Bail out cleanly in that case.
        var sagaInfoCache = serviceProvider.GetService<SagaInfoCache>();
        if (sagaInfoCache is null)
        {
            return;
        }

        var connectionFactory = serviceProvider.GetRequiredService<IConnectionFactory>();
        var sagaMetadata = serviceProvider.GetRequiredService<SagaMetadataCollection>();

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
